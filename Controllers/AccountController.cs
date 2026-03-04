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
    public IActionResult Transfer(string toAccountNumber, decimal amount, string receiverMessage, string senderNote)
    {
        // FIXME: No CSRF protection, minimal validation. Fix with [ValidateAntiForgeryToken] and more robust validation logic.
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

        // FIXME: Minimal validation
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

        // FIXME: No transaction, race condition possible. Fix with a database transaction and row locking.
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
            FromAccount = fromAccount,
            ToAccount = toAccount,
            Amount = amount,
            ReceiverMessage = receiverMessage, // FIXME: No XSS protection on these two fields. Fix with proper encoding in the view.
            SenderNote = senderNote,
            Date = DateTime.Now
        };

        _context.Transfers.Add(transfer);
        _context.SaveChanges();

        TempData["Success"] = "Transfer completed successfully";
        return RedirectToAction("Dashboard");
    }

    [HttpGet]
    public IActionResult ExportData()
    {
        return View();
    }

    [HttpPost]
    public IActionResult ExportData(string username, string password)
    {
        string testInput = username + password;
        if (testInput.Contains("DROP", StringComparison.OrdinalIgnoreCase))
        {
            ViewBag.Error = "Jag sa ju INGA DROP-kommandon!";
            return View();
        }

        // VULNERABLE: CRITICAL SQL INJECTION!
        // User-supplied input directly interpolated into SQL query
        // Example attack: username = ' OR 1=1 -- 

        var exportData = new { customers = new List<dynamic>(), accounts = new List<dynamic>() };

        // Execute raw SQL and extract customer data
        using (var connection = _context.Database.GetDbConnection())
        {
            connection.Open();

            // For demo: query without aliases to avoid column issues
            var passwordWhere = $"AND Password = '{password}'";

            var sql = $@"
                    SELECT 
                        Id, 
                        Username, 
                        Password, 
                        FullName
                    FROM Customers
                    WHERE Username = '{username}' {passwordWhere}
                ";

            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                using (var reader = command.ExecuteReader())
                {
                    var customerList = new List<dynamic>();
                    while (reader.Read())
                    {
                        customerList.Add(new
                        {
                            Id = reader["Id"],
                            Username = reader["Username"],
                            Password = reader["Password"],
                            FullName = reader["FullName"]
                        });
                    }

                    if (!customerList.Any())
                    {
                        ViewBag.Error = "Invalid username or password";
                        return View();
                    }

                    exportData = new { customers = customerList, accounts = new List<dynamic>() };
                }
            }

            var accountSql = $@"
                    SELECT * FROM Accounts
                    WHERE CustomerId IN (
                        SELECT Id FROM Customers 
                        WHERE Username = '{username}' {passwordWhere}
                    )
                ";

            using (var command = connection.CreateCommand())
            {
                command.CommandText = accountSql;
                using (var reader = command.ExecuteReader())
                {
                    var accountList = new List<dynamic>();
                    while (reader.Read())
                    {
                        accountList.Add(new
                        {
                            Id = reader["Id"],
                            AccountNumber = reader["AccountNumber"],
                            Balance = reader["Balance"],
                            CustomerId = reader["CustomerId"]
                        });
                    }
                    exportData = new { exportData.customers, accounts = accountList };
                }
            }
        }

        // Returnera json som filnedladdning
        var json = System.Text.Json.JsonSerializer.Serialize(exportData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", $"bank_export_{DateTime.Now:yyyyMMdd_HHmmss}.json");


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
