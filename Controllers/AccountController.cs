using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using theunsafebank.Data;
using theunsafebank.Models;

namespace theunsafebank.Controllers;

public class AccountController : Controller
{
    private readonly BankContext _context;

    public AccountController(BankContext context)
    {
        _context = context;
    }

    public IActionResult Dashboard()
    {
        // INSECURE
        var customerId = GetCustomerIdFromCookie();
        // var customerId = HttpContext.Session.GetInt32("CustomerId"); // Session-based identity

        if (customerId == null)
        {
            return RedirectToAction("Login", "Auth");
        }

        // Get account with transfers
        var account = _context.Accounts
            .Include(a => a.Customer)
            .Include(a => a.TransfersFrom)
                .ThenInclude(t => t.ToAccount)
            .Include(a => a.TransfersTo)
                .ThenInclude(t => t.FromAccount)
            .FirstOrDefault(a => a.CustomerId == customerId);

        if (account == null)
        {
            return RedirectToAction("Login", "Auth");
        }

        return View(account);
    }

    [HttpPost]
    public IActionResult Transfer(string toAccountNumber, decimal amount, string message)
    {
        // INSECURE: No CSRF protection, minimal validation
        var customerId = GetCustomerIdFromCookie();
        // var customerId = HttpContext.Session.GetInt32("CustomerId"); // Session-based identity

        if (customerId == null)
        {
            return RedirectToAction("Login", "Auth");
        }

        var fromAccount = _context.Accounts
            .FirstOrDefault(a => a.CustomerId == customerId);

        if (fromAccount == null)
        {
            return RedirectToAction("Dashboard");
        }

        // INSECURE: Minimal validation
        if (amount <= 0)
        {
            TempData["Error"] = "Amount must be positive";
            return RedirectToAction("Dashboard");
        }

        var toAccount = _context.Accounts
            .FirstOrDefault(a => a.AccountNumber == toAccountNumber);

        if (toAccount == null)
        {
            TempData["Error"] = "Account not found";
            return RedirectToAction("Dashboard");
        }

        if (fromAccount.Id == toAccount.Id)
        {
            TempData["Error"] = "Cannot transfer to your own account";
            return RedirectToAction("Dashboard");
        }

        // INSECURE: No transaction, race condition possible
        if (fromAccount.Balance < amount)
        {
            TempData["Error"] = "Insufficient funds";
            return RedirectToAction("Dashboard");
        }

        // Perform transfer
        fromAccount.Balance -= amount;
        toAccount.Balance += amount;

        var transfer = new Transfer
        {
            FromAccountId = fromAccount.Id,
            ToAccountId = toAccount.Id,
            Amount = amount,
            Message = message ?? "", // INSECURE: No XSS protection
            Date = DateTime.Now
        };

        _context.Transfers.Add(transfer);
        _context.SaveChanges();

        TempData["Success"] = "Transfer completed successfully";
        return RedirectToAction("Dashboard");
    }

    private int? GetCustomerIdFromCookie()
    {
        if (Request.Cookies.TryGetValue("CustomerId", out var rawValue)
            && int.TryParse(rawValue, out var customerId))
        {
            return customerId;
        }

        return null;
    }
}
