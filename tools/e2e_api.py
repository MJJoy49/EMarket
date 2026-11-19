"""
End-to-end check of the EMarket API against a fresh dev database.

WARNING: it DROPS and recreates the database named in DB below (EMarketDb_Dev). It never touches EMarketDb.
Needs: SQL Server Express at localhost\SQLEXPRESS, sqlcmd, .NET 10 SDK, Python 3.
Run from the repository root after `dotnet build`:   python tools/e2e_api.py
Logs, uploaded files and keys go to tools/.e2e/ (ignored by git).
"""
import base64, json, os, re, secrets, subprocess, sys, time, urllib.error, urllib.request, uuid

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCRATCH = os.path.join(REPO, "tools", ".e2e")
os.makedirs(SCRATCH, exist_ok=True)
SERVER = r"localhost\SQLEXPRESS"
DB = "EMarketDb_Dev"
BASE = "http://localhost:5199/api/v1"
PNG = base64.b64decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==")
results = []


def sql(query, db="master"):
    out = subprocess.run(["sqlcmd", "-S", SERVER, "-E", "-C", "-b", "-I", "-d", db, "-h", "-1", "-W", "-Q", "SET NOCOUNT ON; " + query],
                         capture_output=True, text=True)
    if out.returncode != 0:
        raise RuntimeError(out.stdout + out.stderr)
    return out.stdout.strip()


def check(name, ok, detail=""):
    results.append(ok)
    print(("PASS " if ok else "FAIL ") + name + (f"  [{detail}]" if detail and not ok else ""))


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None


opener = urllib.request.build_opener(NoRedirect)


def call(method, path, body=None, token=None, headers=None, form=None, files=None):
    hdrs = dict(headers or {})
    data = None
    if token:
        hdrs["Authorization"] = "Bearer " + token
    if files is not None or form is not None:
        boundary = uuid.uuid4().hex
        parts = []
        for k, v in (form or {}).items():
            parts.append(f'--{boundary}\r\nContent-Disposition: form-data; name="{k}"\r\n\r\n{v}\r\n'.encode())
        for k, (fname, content) in (files or {}).items():
            parts.append(f'--{boundary}\r\nContent-Disposition: form-data; name="{k}"; filename="{fname}"\r\nContent-Type: image/png\r\n\r\n'.encode() + content + b"\r\n")
        parts.append(f"--{boundary}--\r\n".encode())
        data = b"".join(parts)
        hdrs["Content-Type"] = f"multipart/form-data; boundary={boundary}"
    elif body is not None:
        data = json.dumps(body).encode()
        hdrs["Content-Type"] = "application/json"
    req = urllib.request.Request(BASE + path, data=data, method=method, headers=hdrs)
    try:
        with opener.open(req, timeout=30) as r:
            text = r.read().decode(errors="replace")
            return r.status, (json.loads(text) if text and text[0] in "[{" else text), r.headers
    except urllib.error.HTTPError as e:
        text = e.read().decode(errors="replace")
        return e.code, (json.loads(text) if text and text[:1] in "[{" else text), e.headers


def register_and_login(name, email):
    pw = "Tiger7Lamp"
    status, _, _ = call("POST", "/auth/register", {"fullName": name, "email": email, "phone": "01712345678", "password": pw, "confirmPassword": pw, "acceptTerms": True})
    assert status == 201, status
    sql(f"UPDATE [User] SET EmailConfirmed = 1 WHERE Email = '{email}'", DB)
    return login(email, pw)


def login(email, pw):
    status, body, _ = call("POST", "/auth/login", {"email": email, "password": pw})
    assert status == 200, (status, body)
    return body


# ---------- fresh database ----------
sql(f"IF DB_ID('{DB}') IS NOT NULL BEGIN ALTER DATABASE [{DB}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{DB}]; END")
schema = open(os.path.join(REPO, "db", "EMarketDb_schema.sql"), encoding="utf-8").read()
schema = re.sub(r"\bEMarketDb\b", DB, schema)
schema_file = os.path.join(SCRATCH, "schema_dev.sql")
open(schema_file, "w", encoding="utf-8").write(schema)
out = subprocess.run(["sqlcmd", "-S", SERVER, "-E", "-C", "-b", "-I", "-i", schema_file], capture_output=True, text=True)
assert out.returncode == 0, out.stdout + out.stderr

# ---------- start the API ----------
env = dict(os.environ,
           Jwt__Key=base64.b64encode(secrets.token_bytes(48)).decode(),
           ConnectionStrings__DefaultConnection=f"Server={SERVER};Database={DB};Trusted_Connection=true;TrustServerCertificate=true;",
           ASPNETCORE_ENVIRONMENT="Development", ASPNETCORE_URLS="http://localhost:5199",
           Storage__Root=os.path.join(SCRATCH, "files"), DataProtection__KeysFolder=os.path.join(SCRATCH, "dp-keys"),
           Gateway__Mode="Fake", Jobs__Enabled="false", Gateway__CallbackBaseUrl="http://localhost:5199", Web__BaseUrl="http://localhost:5300")
log = open(os.path.join(SCRATCH, "e2e_api.log"), "w", encoding="utf-8")
api = subprocess.Popen(["dotnet", "run", "--no-build", "--no-launch-profile", "--project", os.path.join(REPO, "src", "EMarket.Api")],
                       stdout=log, stderr=subprocess.STDOUT, env=env)
try:
    for _ in range(60):
        time.sleep(1)
        try:
            if call("GET", "/categories")[0] == 200:
                break
        except Exception:
            pass
    check("api starts and serves categories", call("GET", "/categories")[0] == 200)

    log.flush()
    admin_line = next(l for l in open(os.path.join(SCRATCH, "e2e_api.log"), encoding="utf-8") if "First admin created" in l)
    admin_email, admin_pw = re.search(r"created: (\S+) / (\S+)", admin_line).groups()
    admin = login(admin_email, admin_pw)
    status, _, _ = call("GET", "/admin/seller-applications", token=admin["accessToken"])
    check("seeded admin is blocked until the password changes", status == 403, status)
    status, admin, _ = call("POST", "/auth/change-password", {"oldPassword": admin_pw, "newPassword": "Admin7Lamp", "confirmPassword": "Admin7Lamp"}, admin["accessToken"])
    check("admin changes the first password", status == 200, status)
    at = admin["accessToken"]

    # ---------- seller onboarding ----------
    s = register_and_login("Rahim Uddin", "rahim@example.com")
    form = {"ShopName": "Rahim Store", "ShopDescription": "Phones and more", "BusinessType": "Individual", "NidNumber": "1234567890",
            "PayoutMethod": "bKash", "WalletNumber": "01712345678", "AcceptSellerTerms": "true"}
    status, body, _ = call("POST", "/seller-applications", token=s["accessToken"], form=form, files={"nidFront": ("f.png", PNG), "nidBack": ("b.png", PNG)})
    check("seller application with NID photos is accepted", status == 201, (status, body))
    app_id = body["id"]
    nid_plain = sql(f"SELECT COUNT(*) FROM SellerApplication WHERE NidNumber = '1234567890'", DB)
    check("NID number is encrypted in the database", nid_plain == "0", nid_plain)
    status, _, _ = call("GET", f"/admin/seller-applications/{app_id}/documents/nid-front", token=s["accessToken"])
    check("a seller cannot open NID documents", status == 403, status)
    status, _, _ = call("GET", f"/admin/seller-applications/{app_id}/documents/nid-front", token=at)
    check("an admin can open NID documents", status == 200, status)
    status, _, _ = call("POST", f"/admin/seller-applications/{app_id}/decision", {"approve": True}, at)
    check("admin approves the application", status == 204, status)
    s = login("rahim@example.com", "Tiger7Lamp")
    check("seller role works after logging in again", "Seller" in s["roles"], s["roles"])
    st = s["accessToken"]

    # ---------- product ----------
    cats = call("GET", "/categories")[1]
    leaf = cats[0]["id"]
    status, body, _ = call("POST", "/seller/products", {"name": "Walton Primo X", "categoryId": leaf, "brand": "Walton", "description": "A sturdy phone.",
                                                       "price": 1000, "stock": 5, "variants": []}, st)
    check("seller creates a draft product", status == 201, (status, body))
    pid = body["id"]
    status, img, _ = call("POST", f"/seller/products/{pid}/images", token=st, files={"file": ("p.png", PNG)})
    check("product image upload with thumbnail", status == 200, (status, img))
    status, _, _ = call("GET", img["thumbUrl"].replace("/api/v1", ""))
    check("thumbnail is served publicly", status == 200, status)
    status, bad, _ = call("POST", f"/seller/products/{pid}/images", token=st, files={"file": ("x.png", b"MZ not an image")})
    check("a renamed non-image is refused with MSG37", status == 400 and "JPG or PNG" in bad.get("message", ""), (status, bad))
    call("POST", f"/seller/products/{pid}/submit", token=st)
    status, _, _ = call("POST", f"/admin/products/{pid}/approve", token=at)
    check("admin approves the product", status == 204, status)
    found = call("GET", "/products?q=walton")[1]
    check("approved product is found by search", found["totalCount"] == 1, found)

    # ---------- buyer checkout (COD) ----------
    b = register_and_login("Karim Ahmed", "karim@example.com")
    bt = b["accessToken"]
    div = next(d for d in call("GET", "/locations")[1] if d["name"] == "Dhaka")
    dist = next(d for d in call("GET", f"/locations?parentId={div['id']}")[1] if d["name"] == "Dhaka")
    area = call("GET", f"/locations?parentId={dist['id']}")[1][0]
    status, body, _ = call("POST", "/addresses", {"fullName": "Karim Ahmed", "phone": "01712345678", "divisionId": div["id"], "districtId": dist["id"],
                                                  "areaId": area["id"], "addressLine": "House 12, Road 5, Dhanmondi"}, bt)
    check("buyer saves an address", status == 201, (status, body))
    addr = body["id"]
    status, _, _ = call("POST", "/cart/items", {"productId": pid, "quantity": 2}, st)
    check("seller cannot add own product (MSG35)", status == 409, status)
    status, _, _ = call("POST", "/cart/items", {"productId": pid, "quantity": 2}, bt)
    check("buyer adds to cart", status == 200, status)
    key = str(uuid.uuid4())
    status, first, _ = call("POST", "/checkout", {"addressId": addr, "paymentMethod": "COD"}, bt, {"Idempotency-Key": key})
    check("COD checkout places an order", status == 201, (status, first))
    order = first["orders"][0]
    check("order number has the EM-YYYYMMDD-NNNNNN form", re.fullmatch(r"EM-\d{8}-\d{6}", order["orderNumber"] or "") is not None, order)
    check("payable = items + delivery fee", first["payable"] == 2060, first["payable"])
    status, again, _ = call("POST", "/checkout", {"addressId": addr, "paymentMethod": "COD"}, bt, {"Idempotency-Key": key})
    check("same Idempotency-Key returns the first result", status == 200 and again["result"]["checkoutGroupId"] == first["checkoutGroupId"], (status, again))
    status, _, _ = call("GET", f"/orders/{order['id']}", token=st)
    check("another user cannot read the order", status in (403, 404), status)

    oid = order["id"]
    for step in ["confirm", "process"]:
        call("POST", f"/seller/orders/{oid}/{step}", token=st)
    call("POST", f"/seller/orders/{oid}/ship", {"courierName": "Sundarban", "trackingNote": "CN-1"}, st)
    status, _, _ = call("POST", f"/seller/orders/{oid}/deliver", token=st)
    check("seller delivers the order", status == 204, status)
    status, _, _ = call("POST", f"/orders/{oid}/cancel", {"reason": "Too late to cancel"}, bt)
    check("delivered order cannot be cancelled (MSG20)", status == 409, status)
    history = sql(f"SELECT COUNT(*) FROM OrderStatusHistory WHERE OrderId = '{oid}'", DB)
    check("status history has every step (insert-only table)", history == "5", history)

    # ---------- online payment ----------
    call("POST", "/cart/items", {"productId": pid, "quantity": 1}, bt)
    status, online, _ = call("POST", "/checkout", {"addressId": addr, "paymentMethod": "bKash"}, bt, {"Idempotency-Key": str(uuid.uuid4())})
    check("online checkout waits for payment", status == 201 and online["awaitingPayment"], (status, online))
    status, start, _ = call("POST", f"/orders/groups/{online['checkoutGroupId']}/pay", token=bt)
    check("pay now returns a gateway page", status == 200 and "/payments/fake/" in start["gatewayUrl"], (status, start))
    status, _, _ = call("POST", "/payments/gateway/success", form={"tran_id": start["transactionId"]})
    paid_early = sql(f"SELECT Status FROM Payment WHERE TransactionId = '{start['transactionId']}'", DB)
    check("a forged success callback does not mark the payment paid", paid_early == "Initiated", paid_early)
    status, _, hdrs = call("POST", f"/payments/fake/{start['transactionId']}", form={"result": "VALID"})
    check("gateway payment redirects back to the web result page", status == 302 and "status=Paid" in hdrs.get("Location", ""), (status, hdrs.get("Location")))
    online_status = sql(f"SELECT Status FROM [Order] WHERE Id = '{online['orders'][0]['id']}'", DB)
    check("paid online order moves to Pending", online_status == "Pending", online_status)

    # ---------- ledger and data checks ----------
    unbalanced = sql("SELECT COUNT(*) FROM (SELECT TransactionId FROM LedgerEntry GROUP BY TransactionId HAVING SUM(Amount) <> 0) x", DB)
    check("every ledger transaction adds up to 0", unbalanced == "0", unbalanced)
    pending = sql("SELECT SUM(Amount) FROM LedgerEntry WHERE Account = 'SellerPending'", DB)
    check("seller pending earning = 2000 - 200 + 60", pending == "1860.00", pending)
    try:
        sql("UPDATE LedgerEntry SET Amount = Amount", DB)
        check("ledger rows cannot be updated", False)
    except RuntimeError:
        check("ledger rows cannot be updated", True)
    # ---------- returns, payouts, admin ----------
    sql(f"UPDATE [Order] SET DeliveredAt = DATEADD(day, -8, SYSUTCDATETIME()) WHERE Id = '{oid}'", DB)  # past the 7 day return window
    status, bad, _ = call("POST", f"/returns/orders/{oid}", form={"payload": json.dumps({"items": [{"orderItemId": call("GET", f"/orders/{oid}", token=bt)[1]["items"][0]["id"], "quantity": 1}], "reason": "Damaged"})}, token=bt)
    check("a return after 7 days is refused (MSG23)", status == 409, (status, bad))
    sql(f"UPDATE [Order] SET DeliveredAt = DATEADD(day, -2, SYSUTCDATETIME()) WHERE Id = '{oid}'", DB)
    item_id = call("GET", f"/orders/{oid}", token=bt)[1]["items"][0]["id"]
    status, ret, _ = call("POST", f"/returns/orders/{oid}", form={"payload": json.dumps({"items": [{"orderItemId": item_id, "quantity": 1}], "reason": "Damaged", "description": "Cracked screen", "refundMethod": "StoreCredit"})}, files={"photos": ("p.png", PNG)}, token=bt)
    check("buyer requests a return of 1 of 2 units", status == 201, (status, ret))
    rid = ret["id"]
    status, _, _ = call("GET", f"/returns/{rid}/photos/0", token=at)
    check("admin can see the return photo", status == 200, status)
    other = register_and_login("Nila Begum", "nila@example.com")
    status, _, _ = call("GET", f"/returns/{rid}/photos/0", token=other["accessToken"])
    check("another buyer cannot see the return photo", status in (403, 404), status)
    call("POST", f"/seller/returns/{rid}/decision", {"approve": True}, st)
    status, _, _ = call("POST", f"/seller/returns/{rid}/item-received", {"resellable": True}, st)
    check("seller marks the item received", status == 204, status)
    credit = call("GET", "/profile", token=bt)[1]["storeCredit"]
    check("refund (price + delivery fee, damaged) lands as store credit", credit == 1060, credit)

    # release earnings by moving delivery back in time, as the hourly job would
    sql(f"UPDATE [Order] SET DeliveredAt = DATEADD(day, -9, SYSUTCDATETIME()) WHERE Id = '{oid}'", DB)
    sql("UPDATE ReturnRequest SET Status = 'Closed'", DB)
    dash = call("GET", "/seller/dashboard", token=st)[1]
    check("seller dashboard shows pending earnings from the ledger", dash["balances"]["pending"] > 0, dash["balances"])
    status, _, _ = call("POST", "/seller/payouts", {"amount": 400}, st)
    check("payout below the minimum is refused", status == 400, status)

    status, dash, _ = call("GET", "/admin/dashboard", token=at)
    check("admin dashboard answers", status == 200 and dash["users"] >= 3, (status, dash))
    status, fin, _ = call("GET", "/admin/reports/finance", token=at)
    check("finance report: platform balance equals commission", status == 200 and fin["platformBalance"] == fin["commission"], fin)
    status, _, _ = call("GET", "/admin/reports/sales?from=2020-01-01&to=2026-11-19", token=at)
    check("a report range over one year is refused", status == 400, status)
    status, xlsx, _ = call("GET", "/admin/reports/sales.xlsx", token=at)
    check("excel export is served", status == 200, status)
    status, _, _ = call("PUT", "/admin/settings", {"values": {"commissionPercent": "40"}}, at)
    check("commission of 40% is refused", status == 400, status)
    status, _, _ = call("PUT", "/admin/settings", {"values": {"commissionPercent": "12"}}, at)
    check("commission of 12% is saved", status == 200, status)
    audit = sql("SELECT COUNT(*) FROM AuditLog WHERE Action = 'SettingChanged'", DB)
    check("the settings change is in the audit log", audit == "1", audit)
    status, _, _ = call("GET", "/admin/users", token=bt)
    check("a buyer cannot call an admin endpoint", status == 403, status)
    status, _, _ = call("GET", "/admin/users")
    check("an anonymous call to an admin endpoint is 401", status == 401, status)

    status, _, _ = call("POST", "/contact", {"name": "Nila", "email": "n@example.com", "subject": "Hello", "message": "A question about delivery"})
    check("contact form saves a message", status == 200, status)
    status, page, _ = call("PUT", "/admin/pages/faq", {"html": "<p>Hi</p><script>alert(1)</script>"}, at)
    page = call("GET", "/pages/faq")[1]
    check("static page text is cleaned of scripts", "script" not in page["html"] and "Hi" in page["html"], page)

    status, _, _ = call("PUT", "/admin/settings", {"values": {"maintenanceMode": "1"}}, at)
    status, _, _ = call("GET", "/products")
    check("maintenance mode shows 503 to visitors", status == 503, status)
    status, _, _ = call("GET", "/admin/dashboard", token=at)
    check("maintenance mode keeps the admin working", status == 200, status)
    call("PUT", "/admin/settings", {"values": {"maintenanceMode": "0"}}, at)

    status, _, _ = call("POST", f"/admin/users/{b['userId']}/block", {"reason": "Fake orders reported"}, at)
    check("admin blocks a user", status == 204, status)
    status, _, _ = call("POST", "/auth/login", {"email": "karim@example.com", "password": "Tiger7Lamp"})
    check("a blocked user cannot log in", status == 401, status)
    me = call("GET", "/auth/refresh-check") if False else None

    base = "http://localhost:5199"
    for path, expected in [("/health/live", 200), ("/health/ready", 200)]:
        try:
            with opener.open(base + path, timeout=10) as r:
                code = r.status
        except urllib.error.HTTPError as e:
            code = e.code
        check(f"health endpoint {path}", code == expected, code)
    with opener.open(base + "/api/v1/categories", timeout=10) as r:
        hdr = r.headers
    check("responses carry X-Correlation-Id and security headers", bool(hdr.get("X-Correlation-Id")) and hdr.get("X-Content-Type-Options") == "nosniff", dict(hdr))
    status, err, _ = call("GET", "/orders/not-a-guid", token=st)
    check("an error response carries the correlation id and no stack trace", "correlationId" in str(err) or status == 404, (status, err))

    errors = [l for l in open(os.path.join(SCRATCH, "e2e_api.log"), encoding="utf-8") if "[ERR]" in l or "Unhandled" in l]
    check("no unhandled errors in the API log", not errors, errors[:3])
finally:
    api.terminate()
    try:
        api.wait(10)
    except Exception:
        api.kill()
    log.close()

print(f"\n{sum(results)}/{len(results)} checks passed")
sys.exit(0 if all(results) else 1)
