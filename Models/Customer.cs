namespace theunsafebank.Models;

public class Customer
{
    public int Id { get; set; }
    public string Username { get; set; }
    public string PasswordHashed { get; set; } // Plain text password - INSECURE!
    public string FullName { get; set; }

    // Navigation property
    public Account Account { get; set; }
}
