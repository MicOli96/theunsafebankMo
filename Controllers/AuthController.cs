using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using theunsafebank.Data;
using theunsafebank.Models;

namespace theunsafebank.Controllers;

public class AuthController : Controller
{
    private readonly BankContext _context;

    public AuthController(BankContext context)
    {
        _context = context;
    }

    [HttpGet]
    public IActionResult Login()
    {
        return View();
    }

    [HttpPost]
    public IActionResult Login(string username, string password)
    {
        // FIXME: SQL Injection vulnerable, plain text password comparison. Fix with hashed passwords.
        var customer = _context.Customers
            .FirstOrDefault(c => c.Username == username && c.Password == password);

        if (customer != null)
        {
            // FIXME: Store identity in a plain cookie
            Response.Cookies.Append("CustomerId", customer.Id.ToString());
            // HttpContext.Session.SetInt32("CustomerId", customer.Id); // Session-based identity
            return RedirectToAction("Dashboard", "Account");
        }

        ViewBag.Error = "Invalid username or password";
        return View();
    }

    [HttpGet]
    public IActionResult Register()
    {
        return View();
    }

    [HttpPost]
    public IActionResult Register(string username, string password, string fullName)
    {
        // FIXME: No validation, no password hashing
        var existingCustomer = _context.Customers.FirstOrDefault(c => c.Username == username);

        if (existingCustomer != null)
        {
            ViewBag.Error = "Username already exists";
            return View();
        }

        // Create customer with plain text password
        var customer = new Customer
        {
            Username = username,
            Password = password, // FIXME: Plain text! Use Bcrypt to hash passwords in a real application
            FullName = fullName
        };

        _context.Customers.Add(customer);
        _context.SaveChanges();

        // Generate account number (simple sequential)
        var accountNumber = (1000 + customer.Id).ToString();

        // Create account with starting balance
        var account = new Account
        {
            AccountNumber = accountNumber,
            Balance = 10000m, // 10,000 SEK
            CustomerId = customer.Id
        };

        //Every 10th customer gets an extra 10,000 SEK (for testing purposes)
        if (customer.Id % 10 == 0)
        {
            account.Balance += 10000m;
        }

        _context.Accounts.Add(account);
        _context.SaveChanges();

        // Auto-login after registration (plain cookie - INSECURE)
        Response.Cookies.Append("CustomerId", customer.Id.ToString());
        // HttpContext.Session.SetInt32("CustomerId", customer.Id); // FIXME: Session-based identity (commented)

        return RedirectToAction("Dashboard", "Account");
    }

    public IActionResult Logout()
    {
        // Clear the plain cookie
        Response.Cookies.Delete("CustomerId");
        // HttpContext.Session.Clear(); // FIXME: Session-based identity (commented)
        return RedirectToAction("Login");
    }
}
