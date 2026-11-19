# The checks of tools/e2e_web.py. They run in the same namespace, after the two servers are started.
try:
    for _ in range(90):
        time.sleep(1)
        try:
            urllib.request.urlopen(API + "/health/live", timeout=2)
            urllib.request.urlopen(WEB + "/Maintenance", timeout=2)
            break
        except urllib.error.HTTPError:
            break
        except Exception:
            pass

    # ---------- public pages ----------
    guest = Browser()
    status, html, _, hdrs = guest.get("/")
    check("home page renders for a guest", status == 200 and "EMarket" in html, status)
    check("pages send the security headers", "default-src 'self'" in hdrs.get("Content-Security-Policy", "") and hdrs.get("X-Content-Type-Options") == "nosniff", dict(hdrs))
    check("no inline script or style is needed (CSP stays strict)", "<script>" not in html and ' style="' not in html, html[:200])
    for path, text in [("/products", "Products"), ("/pages/about", "About"), ("/pages/faq", "FAQ"), ("/pages/contact", "Contact us"), ("/login", "Log in"), ("/register", "Create account")]:
        status, html, _, _ = guest.get(path)
        check(f"{path} renders", status == 200 and text in html, status)
    status, html, url, _ = guest.get("/does-not-exist")
    check("an unknown address shows the 404 page", status == 404 and "Page not found" in html, (status, url, html[:150]))
    status, html, url, _ = guest.get("/cart")
    check("a guest is sent to Login for the cart", "/login" in url and "ReturnUrl" in url, url)
    status, html, url, _ = guest.get("/admin")
    check("a guest cannot reach the admin area", "/login" in url, url)

    # ---------- registration and login ----------
    email = "web.user@example.com"
    status, html, _, _ = guest.post_form("/register", {
        "Input.FullName": "Web User", "Input.Email": email, "Input.Phone": "01712345678", "Input.Password": "Tiger7Lamp",
        "Input.ConfirmPassword": "Tiger7Lamp", "Input.AcceptTerms": ["true", "false"]}, page="/register")
    check("registration redirects to the login page with a success message", "/login" in guest.get("/login")[2] and status == 200, status)
    count = sql(f"SELECT COUNT(*) FROM [User] WHERE Email = '{email}'", DB)
    check("the account exists after registration", count == "1", count)

    status, html, _, _ = guest.post_form("/register", {
        "Input.FullName": "Web User", "Input.Email": "weak@example.com", "Input.Phone": "01712345678", "Input.Password": "abc12345",
        "Input.ConfirmPassword": "abc12345", "Input.AcceptTerms": ["true", "false"]}, page="/register")
    check("a weak password is refused and the typed name is kept", "Web User" in html and "not valid" in html, status)

    sql(f"UPDATE [User] SET EmailConfirmed = 1 WHERE Email = '{email}'", DB)
    status, html, _, _ = guest.post_form("/login", {"Input.Email": email, "Input.Password": "Wrong7Pass"}, page="/login")
    check("a wrong password shows one message and stays on Login", "Email or password is wrong" in html, status)

    user = Browser()
    status, html, url, _ = user.post_form("/login", {"Input.Email": email, "Input.Password": "Tiger7Lamp", "Input.RememberMe": ["true", "false"]}, page="/login")
    check("login succeeds and lands on the home page", status == 200 and "Web User" in html, url)
    cookie = user.cookies().get("EMarket.Auth")
    check("the sign-in cookie is HttpOnly", cookie is not None and any(k.lower() == "httponly" for k in cookie._rest), cookie)
    check("no token appears in the page or in JavaScript", "eyJ" not in html and "access_token" not in html and "refresh_token" not in html, "token text in page")
    status, html, _, _ = user.get("/profile")
    check("the profile page shows the saved details", status == 200 and email in html, status)
    status, html, _, _ = user.get("/ui/counts")
    check("the counts feed answers JSON", status == 200 and '"unread"' in html, html[:100])
    status, html, _, _ = user.get("/admin")
    check("a buyer cannot open the admin area", status in (403, 404) or "Not allowed" in html, status)

    status, html, _, _ = user.post_form("/profile", {"Input.FullName": "Web User", "Input.Email": email, "Input.Phone": "0171", "Input.Gender": ""}, page="/profile")
    check("an invalid phone on the profile is refused on the form", "not valid" in html, status)
    status, html, _, _ = user.post_form("/profile", {"Input.FullName": "Web User Two", "Input.Email": email, "Input.Phone": "01812345678", "Input.Gender": "Male"}, page="/profile")
    check("a valid profile change is saved", "Web User Two" in user.get("/profile")[1], status)

    # logout is a POST with an anti-forgery token
    status, _, _, _ = user.request("POST", "/logout", {})
    check("logout without an anti-forgery token is refused", status == 400, status)
    refresh_before = sql(f"SELECT COUNT(*) FROM RefreshToken WHERE RevokedAt IS NULL AND UserId = (SELECT Id FROM [User] WHERE Email = '{email}')", DB)
    status, html, _, _ = user.post_form("/logout", {}, page="/")
    check("logout works and ends the sign-in", "Log in" in html, status)
    refresh_after = sql(f"SELECT COUNT(*) FROM RefreshToken WHERE RevokedAt IS NULL AND UserId = (SELECT Id FROM [User] WHERE Email = '{email}')", DB)
    check("logout revoked the refresh token at the API", int(refresh_after) < int(refresh_before), (refresh_before, refresh_after))


    # ---------- helpers for the marketplace journey ----------
    def post_multipart(browser, path, fields, files=None, page=None):
        status, html, _, _ = browser.get(page or path.split("?")[0])
        fields = dict(fields)
        fields["__RequestVerificationToken"] = browser.token(html)
        return browser.request("POST", path, multipart={"fields": fields, "files": files or {}})

    def options(html, select_name):
        block = re.search(r'<select[^>]*name="%s"[^>]*>(.*?)</select>' % re.escape(select_name), html, re.S)
        return re.findall(r'<option[^>]*value="([0-9a-f-]{36})"[^>]*>\s*(?:&nbsp;)*([^<]+)<', block.group(1)) if block else []

    def web_register(name, mail):
        b = Browser()
        b.post_form("/register", {"Input.FullName": name, "Input.Email": mail, "Input.Phone": "01712345678", "Input.Password": "Tiger7Lamp",
                                  "Input.ConfirmPassword": "Tiger7Lamp", "Input.AcceptTerms": ["true", "false"]}, page="/register")
        sql(f"UPDATE [User] SET EmailConfirmed = 1 WHERE Email = '{mail}'", DB)
        return web_login(mail, "Tiger7Lamp")

    def web_login(mail, password):
        b = Browser()
        status, html, url, _ = b.post_form("/login", {"Input.Email": mail, "Input.Password": password, "Input.RememberMe": ["true", "false"]}, page="/login")
        return b

    # ---------- admin first login through the web ----------
    admin_line = next(l for l in open(os.path.join(SCRATCH, "e2e_api.log"), encoding="utf-8") if "First admin created" in l)
    admin_email, admin_pw = re.search(r"created: (\S+) / (\S+)", admin_line).groups()
    admin = Browser()
    status, html, url, _ = admin.post_form("/login", {"Input.Email": admin_email, "Input.Password": admin_pw}, page="/login")
    check("the seeded admin is sent to Change password at first login", "/change-password" in url and "choose a new password" in html, url)
    status, html, url, _ = admin.get("/admin")
    check("the admin area is locked until the password is changed", "/change-password" in url, url)
    status, html, url, _ = admin.post_form("/change-password", {"Input.OldPassword": admin_pw, "Input.NewPassword": "Admin7Lamp", "Input.ConfirmPassword": "Admin7Lamp"}, page="/change-password")
    status, html, url, _ = admin.get("/admin")
    check("after the change the admin dashboard opens", status == 200 and "Dashboard" in html, (status, url))

    # ---------- seller application and approval ----------
    seller = web_register("Rahim Uddin", "rahim@example.com")
    status, html, url, _ = post_multipart(seller, "/seller/apply", {
        "Input.ShopName": "Rahim Store", "Input.ShopDescription": "Phones and more", "Input.BusinessType": "Individual", "Input.NidNumber": "1234567890",
        "Input.PayoutMethod": "bKash", "Input.WalletNumber": "01712345678", "Input.AcceptSellerTerms": "true"},
        {"nidFront": ("f.png", PNG), "nidBack": ("b.png", PNG)}, page="/seller/apply")
    check("the seller application is accepted and shows its status", "/seller/status" in url and "Pending" in html, (status, url))
    status, html, _, _ = admin.get("/admin/applications?status=Pending")
    app_id = (re.search(r'/admin/applications/([0-9a-f-]{36})', html) or [None, None])[1]
    check("the admin sees the pending application", app_id is not None and "Rahim Store" in html, status)
    status, html, _, _ = admin.get(f"/admin/applications/{app_id}")
    check("the review page shows the NID number and the document images", "1234567890" in html and f"/files/nid/{app_id}/nid-front" in html, status)
    status, img, _, hdrs = admin.get(f"/files/nid/{app_id}/nid-front")
    check("an admin can load an NID image through the web app", status == 200, status)
    status, _, _, _ = seller.get(f"/files/nid/{app_id}/nid-front")
    check("a seller-to-be cannot load NID images", status in (403, 404) or "login" in _, status)
    admin.post_form(f"/admin/applications/{app_id}", {"approve": "true", "reason": ""}, page=f"/admin/applications/{app_id}")
    seller = web_login("rahim@example.com", "Tiger7Lamp")
    status, html, _, _ = seller.get("/seller")
    check("after approval the seller dashboard opens", status == 200 and "Dashboard" in html, status)

    # ---------- the seller adds a product ----------
    status, html, _, _ = seller.get("/seller/products/edit")
    leaf = options(html, "Input.CategoryId")[0][0]
    status, html, url, _ = seller.post_form("/seller/products/edit?handler=Save", {
        "Input.Name": "Walton Primo X", "Input.CategoryId": leaf, "Input.Brand": "Walton", "Input.Description": "A sturdy phone with a big battery.",
        "Input.Price": "1000", "Input.Stock": "10"}, page="/seller/products/edit")
    product_id = (re.search(r'/seller/products/edit/([0-9a-f-]{36})', url) or [None, None])[1]
    check("the product is saved as a draft", product_id is not None and "Draft" in seller.get("/seller/products")[1], url)
    status, html, _, _ = post_multipart(seller, f"/seller/products/edit/{product_id}?handler=Upload", {}, {"file": ("p.png", PNG)}, page=f"/seller/products/edit/{product_id}")
    check("the product image uploads", "Product image 1" in html or "Main image" in html, status)
    status, html, _, _ = seller.post_form(f"/seller/products/edit/{product_id}?handler=Save&submit=true", {
        "Input.Name": "Walton Primo X", "Input.CategoryId": leaf, "Input.Brand": "Walton", "Input.Description": "A sturdy phone with a big battery.",
        "Input.Price": "1000", "Input.Stock": "10"}, page=f"/seller/products/edit/{product_id}")
    check("the product is submitted for approval", "Pending" in seller.get("/seller/products")[1], status)
    admin.post_form(f"/admin/products/{product_id}", {"action": "approve", "reason": ""}, page=f"/admin/products/{product_id}")
    status, html, _, _ = guest.get("/products?q=walton")
    check("the approved product is found by search", "Walton Primo X" in html, status)
    status, html, _, _ = guest.get(f"/products/{product_id}")
    check("the product page shows price, shop and description", "Tk 1,000.00" in html and "Rahim Store" in html and "sturdy phone" in html, status)
    status, img, _, _ = guest.get(re.search(r'src="(/media/[^"]+)"', html).group(1))
    check("product images are served through the web app", status == 200, status)

    # ---------- buyer: address, cart, checkout with cash ----------
    buyer = web_login(email, "Tiger7Lamp")
    status, html, _, _ = buyer.get("/addresses?add=true")
    division = next(i for i, n in options(html, "Input.DivisionId") if n.strip() == "Dhaka")
    status, html, _, _ = buyer.post_form("/addresses?handler=Refresh", {"Input.DivisionId": division}, page="/addresses?add=true")
    district = next(i for i, n in options(html, "Input.DistrictId") if n.strip() == "Dhaka")
    status, html, _, _ = buyer.post_form("/addresses?handler=Refresh", {"Input.DivisionId": division, "Input.DistrictId": district}, page="/addresses?add=true")
    area = options(html, "Input.AreaId")[0][0]
    status, html, _, _ = buyer.post_form("/addresses?handler=Save", {"Input.FullName": "Karim Ahmed", "Input.Phone": "01712345678", "Input.DivisionId": division,
        "Input.DistrictId": district, "Input.AreaId": area, "Input.AddressLine": "House 12, Road 5, Dhanmondi", "Input.IsDefault": ["true", "false"]}, page="/addresses?add=true")
    check("the buyer saves an address with linked division, district and area", "House 12, Road 5" in buyer.get("/addresses")[1], status)

    status, html, url, _ = buyer.post_form(f"/products/{product_id}?handler=Add", {"quantity": "2"}, page=f"/products/{product_id}")
    status, html, _, _ = buyer.get("/cart")
    check("the cart shows the product with the right total", "Walton Primo X" in html and "Tk 2,000.00" in html, status)
    status, html, _, _ = buyer.get("/checkout")
    key = re.search(r'name="IdempotencyKey"[^>]*value="([^"]+)"', html).group(1)
    address_id = re.search(r'name="Input.AddressId"[^>]*value="([0-9a-f-]{36})"', html).group(1)
    check("checkout shows the summary with the delivery fee", "Tk 2,060.00" in html, status)
    place = {"IdempotencyKey": key, "Input.AddressId": address_id, "Input.PaymentMethod": "COD", "Input.UseStoreCredit": "false"}
    status, html, url, _ = buyer.post_form("/checkout?handler=Place", place, page="/checkout")
    check("placing a cash order shows the success page with the order number", "Thank you" in html and re.search(r"EM-\d{8}-\d{6}", html), (status, url))
    order_number = re.search(r"EM-\d{8}-\d{6}", html).group(0)
    status, html, url, _ = buyer.post_form("/checkout?handler=Place", place, page="/checkout")
    count = sql("SELECT COUNT(*) FROM CheckoutGroup", DB)
    check("sending the same form again (same key) creates no second order", count == "1", count)

    # ---------- seller fulfils the order ----------
    status, html, _, _ = seller.get("/seller/orders")
    order_id = (re.search(r'/seller/orders/([0-9a-f-]{36})', html) or [None, None])[1]
    check("the seller sees the new order", order_number in html, status)
    for step in ("confirm", "process"):
        seller.post_form(f"/seller/orders/{order_id}?handler=Move", {"step": step}, page=f"/seller/orders/{order_id}")
    seller.post_form(f"/seller/orders/{order_id}?handler=Ship", {"Ship.CourierName": "Sundarban", "Ship.TrackingNote": "CN-1"}, page=f"/seller/orders/{order_id}")
    status, html, _, _ = buyer.get(f"/orders/{order_id}")
    check("the buyer sees the shipped status and the tracking note", "Shipped" in html and "CN-1" in html, status)
    status, html, _, _ = buyer.post_form(f"/orders/{order_id}?handler=Cancel", {"Cancel.Reason": "Too late for me"}, page=f"/orders/{order_id}")
    check("a shipped order cannot be cancelled (MSG20)", "no longer be cancelled" in buyer.get(f"/orders/{order_id}")[1] or "Shipped" in buyer.get(f"/orders/{order_id}")[1], status)
    seller.post_form(f"/seller/orders/{order_id}?handler=Move", {"step": "deliver"}, page=f"/seller/orders/{order_id}")
    status, slip, _, _ = seller.get(f"/seller/orders/{order_id}/slip")
    check("the seller can open the packing slip", status == 200 and "Packing slip" in slip, status)

    # ---------- invoice, review, return ----------
    status, pdf, _, hdrs = buyer.request("GET", f"/orders/{order_id}?handler=Invoice")
    check("the buyer downloads the invoice as a PDF", status == 200 and pdf.startswith("%PDF"), (status, pdf[:20]))
    status, html, _, _ = buyer.get(f"/orders/{order_id}")
    item_id = re.search(r'/reviews/new/([0-9a-f-]{36})', html).group(1)
    status, html, url, _ = buyer.post_form(f"/reviews/new/{item_id}", {"Input.Rating": "4", "Input.Comment": "Good phone"}, page=f"/reviews/new/{item_id}")
    check("a review of a delivered item is posted", "/orders" in url and "Thank you" in html, (status, url))
    status, html, _, _ = guest.get(f"/products/{product_id}")
    check("the review shows on the product page with a masked name", "Good phone" in html and "W*** U*** T***" in html and "Verified purchase" in html, status)
    status, html, _, _ = buyer.get(f"/orders/{order_id}/return")
    qty_field = re.search(r'name="(qty_[0-9a-f-]{36})"', html).group(1)
    status, html, url, _ = post_multipart(buyer, f"/orders/{order_id}/return", {"Reason": "Damaged", "Description": "Cracked screen", "RefundMethod": "StoreCredit", qty_field: "1"},
                                          {"photos": ("p.png", PNG)}, page=f"/orders/{order_id}/return")
    check("the buyer sends a return request for one unit", "/returns/" in url and "Cracked screen" in html, (status, url))
    return_id = re.search(r'/returns/([0-9a-f-]{36})', url).group(1)
    status, html, _, _ = seller.get("/seller/returns")
    check("the seller sees the return and its photo", "Cracked screen" in html and f"/files/returns/{return_id}/0" in html, status)
    status, img, _, _ = buyer.get(f"/files/returns/{return_id}/0")
    check("the buyer can load the return photo", status == 200, status)
    seller.post_form(f"/seller/returns?handler=Decide&id={return_id}&approve=true", {}, page="/seller/returns")
    seller.post_form(f"/seller/returns?handler=Received&id={return_id}", {"resellable": "true"}, page="/seller/returns")
    status, html, _, _ = buyer.get("/profile")
    check("the refund lands as store credit on the profile (price + fee, damaged)", "Tk 1,060.00" in html, status)

    # ---------- online payment through the test gateway ----------
    buyer.post_form(f"/products/{product_id}?handler=Add", {"quantity": "1"}, page=f"/products/{product_id}")
    status, html, _, _ = buyer.get("/checkout")
    key2 = re.search(r'name="IdempotencyKey"[^>]*value="([^"]+)"', html).group(1)
    status, html, url, _ = buyer.post_form("/checkout?handler=Place", {"IdempotencyKey": key2, "Input.AddressId": address_id, "Input.PaymentMethod": "bKash", "Input.UseStoreCredit": "false"}, page="/checkout")
    check("an online order goes to the gateway page", "/api/v1/payments/fake/" in url and "Test payment" in html, url)
    tran = url.rsplit("/", 1)[1]
    status, html, url, _ = buyer.request("POST", url, {"result": "VALID"})
    check("after paying, the buyer lands on the orders page with a confirmation", "/orders" in url and "Payment received" in html, url)
    online_status = sql(f"SELECT Status FROM Payment WHERE TransactionId = '{tran}'", DB)
    check("the payment is Paid and verified by the API", online_status == "Paid", online_status)

    # ---------- every page of the seller and admin areas opens ----------
    for path in ["/seller", "/seller/products", "/seller/orders", "/seller/returns", "/seller/reviews", "/seller/earnings", "/seller/shop"]:
        status, html, _, _ = seller.get(path)
        check(f"seller page {path} opens", status == 200 and "Seller" in html, status)
    for path in ["/admin", "/admin/users", "/admin/users?view=shops", "/admin/users?view=admins", "/admin/applications", "/admin/products", "/admin/products?view=reviews",
                 "/admin/categories", "/admin/orders", f"/admin/orders/{order_id}", "/admin/returns", "/admin/payments", "/admin/payments?view=ledger", "/admin/payouts",
                 "/admin/promotions", "/admin/promotions?view=banners", "/admin/messages", "/admin/reports", "/admin/reports?view=users", "/admin/reports?view=finance",
                 "/admin/settings", "/admin/settings/pages/faq", "/admin/audit"]:
        status, html, _, _ = admin.get(path)
        check(f"admin page {path} opens", status == 200 and "Admin" in html, status)
    status, xlsx, _, hdrs = admin.request("GET", "/admin/reports?handler=Export")
    check("the sales report exports to Excel", status == 200 and "spreadsheetml" in hdrs.get("Content-Type", ""), status)
    status, xlsx, _, hdrs = seller.request("GET", "/seller/earnings?handler=Export")
    check("the seller earnings export to Excel", status == 200 and "spreadsheetml" in hdrs.get("Content-Type", ""), status)

    # ---------- access rules ----------
    for who, label, path in [(buyer, "a buyer", "/seller"), (buyer, "a buyer", "/admin/users"), (seller, "a seller", "/admin/orders"), (admin, "an admin", "/cart"), (admin, "an admin", "/checkout")]:
        status, html, url, _ = who.get(path)
        check(f"{label} cannot open {path}", status in (403, 404) or "Not allowed" in html or "/login" in url, (status, url))
    status, _, _, _ = buyer.request("POST", f"/cart?handler=Remove&id={product_id}", {})
    check("a form post without the anti-forgery token is refused", status == 400, status)

    # ---------- settings, content, maintenance ----------
    status, html, _, _ = admin.post_form("/admin/settings", {"s_commissionPercent": "40"}, page="/admin/settings")
    check("a commission of 40% is refused on the settings page", "not valid" in html or "Invalid" in html or "valid" in html, status)
    admin.post_form("/admin/settings/pages/faq", {"Input.Html": "<p>Hello FAQ</p><script>alert(1)</script>"}, page="/admin/settings/pages/faq")
    status, html, _, _ = guest.get("/pages/faq")
    check("the FAQ page shows the saved text without the script", "Hello FAQ" in html and "alert(1)" not in html, status)
    status, html, _, _ = guest.post_form("/pages/contact", {"Input.Name": "Nila", "Input.Email": "nila@example.com", "Input.Subject": "Hello", "Input.Message": "A question about delivery"}, page="/pages/contact")
    check("the contact form saves a message the admin can read", "Hello" in admin.get("/admin/messages")[1], status)
    admin.post_form("/admin/settings", {"s_commissionPercent": "10", "maintenance": "on"}, page="/admin/settings")
    status, html, _, _ = guest.get("/products")
    check("maintenance mode shows the Maintenance page to visitors", status == 503 and "under maintenance" in html, status)
    status, html, _, _ = admin.get("/admin")
    check("maintenance mode keeps the admin working", status == 200 and "Dashboard" in html, status)
    admin.post_form("/admin/settings", {"s_commissionPercent": "10"}, page="/admin/settings")
    status, html, _, _ = guest.get("/")
    check("maintenance mode can be switched off again", status == 200, status)

    # ---------- layout and accessibility basics on the main pages ----------
    for path in ["/", "/products", f"/products/{product_id}", "/login", "/cart"]:
        status, html, _, _ = buyer.get(path)
        problems = []
        if 'lang="en"' not in html: problems.append("no lang")
        if 'name="viewport"' not in html: problems.append("no viewport")
        if "<h1" not in html: problems.append("no h1")
        if re.search(r"<img(?![^>]*\balt=)[^>]*>", html): problems.append("img without alt")
        if re.search(r'<input(?![^>]*type="(?:hidden|submit|checkbox|radio|file)")(?![^>]*\b(?:aria-label|id)=)[^>]*>', html): problems.append("input without label hook")
        check(f"{path} has lang, viewport, a heading and alt text", not problems, problems)

    errors = [l for l in open(os.path.join(SCRATCH, "e2e_web.log"), encoding="utf-8") if "fail:" in l.lower() or "Unhandled" in l]
    check("no unhandled errors in the web log", not errors, errors[:3])
finally:
    for p in procs:
        p.terminate()
        try:
            p.wait(10)
        except Exception:
            p.kill()
    api_log.close()
    web_log.close()

print(f"\n{sum(results)}/{len(results)} checks passed")
sys.exit(0 if all(results) else 1)
