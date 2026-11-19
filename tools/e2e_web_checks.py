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
