"""
End-to-end check of the EMarket web app (Razor Pages) running against the API and a fresh dev database.

WARNING: it DROPS and recreates EMarketDb_Dev (never EMarketDb).
Needs: SQL Server Express at localhost\\SQLEXPRESS, sqlcmd, .NET 10 SDK, Python 3.
Run from the repository root after `dotnet build`:   python tools/e2e_web.py
"""
import base64, http.cookiejar, json, os, re, secrets, subprocess, sys, time, urllib.error, urllib.parse, urllib.request

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCRATCH = os.path.join(REPO, "tools", ".e2e")
os.makedirs(SCRATCH, exist_ok=True)
SERVER = r"localhost\SQLEXPRESS"
DB = "EMarketDb_Dev"
API = "http://localhost:5199"
WEB = "http://localhost:5300"
PNG = base64.b64decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==")
results = []


def sql(query, db="master"):
    out = subprocess.run(["sqlcmd", "-S", SERVER, "-E", "-C", "-b", "-I", "-d", db, "-h", "-1", "-W", "-Q", "SET NOCOUNT ON; " + query], capture_output=True, text=True)
    if out.returncode != 0:
        raise RuntimeError(out.stdout + out.stderr)
    return out.stdout.strip()


def check(name, ok, detail=""):
    results.append(ok)
    print(("PASS " if ok else "FAIL ") + name + (f"  [{str(detail)[:300]}]" if detail and not ok else ""))


class Browser:
    """A tiny browser: keeps cookies, follows redirects, and fills forms with the anti-forgery token."""

    def __init__(self):
        self.jar = http.cookiejar.CookieJar()
        self.opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(self.jar))

    def request(self, method, path, data=None, headers=None, multipart=None):
        url = path if path.startswith("http") else WEB + path
        body = None
        hdrs = dict(headers or {})
        if multipart:
            boundary = secrets.token_hex(8)
            parts = []
            for k, v in multipart.get("fields", {}).items():
                parts.append(f'--{boundary}\r\nContent-Disposition: form-data; name="{k}"\r\n\r\n{v}\r\n'.encode())
            for k, (name, content) in multipart.get("files", {}).items():
                parts.append(f'--{boundary}\r\nContent-Disposition: form-data; name="{k}"; filename="{name}"\r\nContent-Type: image/png\r\n\r\n'.encode() + content + b"\r\n")
            parts.append(f"--{boundary}--\r\n".encode())
            body = b"".join(parts)
            hdrs["Content-Type"] = f"multipart/form-data; boundary={boundary}"
        elif data is not None:
            body = urllib.parse.urlencode(data, doseq=True).encode()
            hdrs["Content-Type"] = "application/x-www-form-urlencoded"
        req = urllib.request.Request(url, data=body, method=method, headers=hdrs)
        try:
            with self.opener.open(req, timeout=60) as r:
                return r.status, r.read().decode(errors="replace"), r.geturl(), r.headers
        except urllib.error.HTTPError as e:
            return e.code, e.read().decode(errors="replace"), url, e.headers

    def get(self, path):
        return self.request("GET", path)

    def token(self, html):
        m = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', html)
        return m.group(1) if m else ""

    def post_form(self, path, fields, page=None):
        """GET the page for its anti-forgery token, then post the fields."""
        status, html, _, _ = self.get(page or path)
        data = dict(fields)
        data["__RequestVerificationToken"] = self.token(html)
        return self.request("POST", path, data)

    def cookies(self):
        return {c.name: c for c in self.jar}


# ---------- fresh database ----------
sql(f"IF DB_ID('{DB}') IS NOT NULL BEGIN ALTER DATABASE [{DB}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{DB}]; END")
schema = re.sub(r"\bEMarketDb\b", DB, open(os.path.join(REPO, "db", "EMarketDb_schema.sql"), encoding="utf-8").read())
schema_file = os.path.join(SCRATCH, "schema_dev.sql")
open(schema_file, "w", encoding="utf-8").write(schema)
out = subprocess.run(["sqlcmd", "-S", SERVER, "-E", "-C", "-b", "-I", "-i", schema_file], capture_output=True, text=True)
assert out.returncode == 0, out.stdout + out.stderr

base_env = dict(os.environ, ASPNETCORE_ENVIRONMENT="Development", Jobs__Enabled="false")
api_env = dict(base_env, Jwt__Key=base64.b64encode(secrets.token_bytes(48)).decode(),
               ConnectionStrings__DefaultConnection=f"Server={SERVER};Database={DB};Trusted_Connection=true;TrustServerCertificate=true;",
               ASPNETCORE_URLS=API, Storage__Root=os.path.join(SCRATCH, "files"), DataProtection__KeysFolder=os.path.join(SCRATCH, "dp-keys"),
               Gateway__Mode="Fake", Gateway__CallbackBaseUrl=API, Web__BaseUrl=WEB)
web_env = dict(base_env, ASPNETCORE_URLS=WEB, Api__BaseUrl=API, Csp__FormActions=API)
api_log = open(os.path.join(SCRATCH, "e2e_api.log"), "w", encoding="utf-8")
web_log = open(os.path.join(SCRATCH, "e2e_web.log"), "w", encoding="utf-8")
procs = [
    subprocess.Popen(["dotnet", "run", "--no-build", "--no-launch-profile", "--project", os.path.join(REPO, "src", "EMarket.Api")], stdout=api_log, stderr=subprocess.STDOUT, env=api_env),
    subprocess.Popen(["dotnet", "run", "--no-build", "--no-launch-profile", "--project", os.path.join(REPO, "src", "EMarket.Web")], stdout=web_log, stderr=subprocess.STDOUT, env=web_env),
]

exec(open(os.path.join(REPO, "tools", "e2e_web_checks.py"), encoding="utf-8").read())
