using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Data.Sqlite;

namespace FiscalReceiptParser.Data
{
    public static class Database
    {
        private const string DbName = "EISPointOfSaleDb.db";
        private static readonly string DbPath;

        static Database()
        {
            // Java checked for a jdwp debugger flag to detect "development" mode.
            // Debugger.IsAttached is the .NET equivalent (true when running from
            // Visual Studio with F5 / a debugger attached).
            bool isDevelopment = Debugger.IsAttached;

            if (isDevelopment)
            {
                DbPath = Path.Combine(Directory.GetCurrentDirectory(), "MQPointOfSale", DbName);
            }
            else
            {
                DbPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "POSSetup",
                    DbName);
            }

            CreateDirectoryIfNotExists(DbPath);
        }

        private static void CreateDirectoryIfNotExists(string dbFilePath)
        {
            var directory = Path.GetDirectoryName(dbFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        // 1. This is the single source of truth for creating a connection string instance
        public static SqliteConnection CreateConnection()
        {
            return new SqliteConnection($"Data Source={DbPath}"); 
        }

        // 2. This simply reuses the factory method above and opens it synchronously
        public static SqliteConnection ConnOpen()
        {
            var connection = CreateConnection();
            connection.Open();
            return connection;
        }

        /// <summary>
        /// Creates all tables if they don't already exist. Safe to call every time
        /// the app starts — CREATE TABLE IF NOT EXISTS is a no-op on subsequent runs,
        /// same as the Java version.
        /// </summary>
        public static void InitializeDatabase()
        {
            try
            {
                using var conn = ConnOpen();

                using (var pragmaCmd = conn.CreateCommand())
                {
                    pragmaCmd.CommandText = "PRAGMA foreign_keys = ON;";
                    pragmaCmd.ExecuteNonQuery();
                }

                // Same order as the Java version, preserved deliberately so that
                // every FOREIGN KEY references a table that was already created.
                string[] createStatements =
                {
                    @"CREATE TABLE IF NOT EXISTS Products (
                        ProductCode TEXT NOT NULL PRIMARY KEY,
                        ProductName TEXT NOT NULL,
                        Description TEXT NOT NULL,
                        Quantity REAL NOT NULL,
                        UnitOfMeasure TEXT,
                        Price REAL NOT NULL,
                        SiteId TEXT,
                        ProductExpiryDate TEXT,
                        MinimumStockLevel REAL,
                        TaxRateId TEXT,
                        IsProduct INTEGER,
                        Discount REAL DEFAULT 0.0
                    )",

                    @"CREATE TABLE IF NOT EXISTS Users (
                        UserID INTEGER PRIMARY KEY AUTOINCREMENT,
                        FirstName TEXT NOT NULL,
                        LastName TEXT NOT NULL,
                        UserName TEXT NOT NULL UNIQUE,
                        Gender TEXT CHECK (Gender IN ('MALE', 'FEMALE')),
                        PhoneNumber TEXT,
                        EmailAddress TEXT UNIQUE,
                        Address TEXT,
                        Role TEXT NOT NULL CHECK (Role IN ('ADMIN', 'CASHIER')),
                        Password TEXT NOT NULL
                    )",

                    @"CREATE TABLE IF NOT EXISTS Invoices (
                        InvoiceNumber TEXT PRIMARY KEY,
                        InvoiceDateTime TEXT,
                        InvoiceTotal REAL,
                        SellerTin TEXT,
                        BuyerTin TEXT,
                        TotalVAT REAL,
                        OfflineTransactionSignature TEXT,
                        SiteId TEXT,
                        ValidationUrl TEXT,
                        IsReliefSupply INTEGER,
                        State INTEGER,
                        PaymentId TEXT,
                        AmountPaid REAL
                    )",

                    @"CREATE TABLE IF NOT EXISTS LineItems (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        InvoiceNumber TEXT,
                        ProductCode TEXT,
                        Description TEXT,
                        Quantity REAL,
                        TaxRateID TEXT,
                        Discount REAL,
                        UnitPrice REAL,
                        TotalPrice REAL,
                        DiscountAmount REAL,
                        VATRate REAL,
                        IsProduct INTEGER,
                        VATAmount REAL,
                        FOREIGN KEY(InvoiceNumber) REFERENCES Invoices(InvoiceNumber)
                    )",

                    @"CREATE TABLE IF NOT EXISTS Discounts (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        DiscountType TEXT,
                        DiscountValue REAL,
                        DiscountReason TEXT,
                        InvoiceNumber TEXT,
                        FOREIGN KEY(InvoiceNumber) REFERENCES Invoices(InvoiceNumber)
                    )",

                    @"CREATE TABLE IF NOT EXISTS TerminalSites (
                        SiteId TEXT PRIMARY KEY,
                        Name TEXT,
                        Location TEXT
                    )",

                    @"CREATE TABLE IF NOT EXISTS TaxRates (
                        Id TEXT PRIMARY KEY,
                        Name TEXT,
                        ChargeMode TEXT,
                        Ordinal INTEGER,
                        Rate REAL
                    )",

                    @"CREATE TABLE IF NOT EXISTS VoidReceiptRequests (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        InvoiceNumber TEXT,
                        RequestReason TEXT,
                        RequestDate TEXT,
                        FOREIGN KEY(InvoiceNumber) REFERENCES Invoices(InvoiceNumber)
                    )",

                    @"CREATE TABLE IF NOT EXISTS ActivatedTerminal (
                        TerminalId TEXT PRIMARY KEY,
                        TerminalPosition INTEGER,
                        TaxpayerId INTEGER,
                        ActivationDate TEXT,
                        IsActive INTEGER,
                        JwtToken TEXT,
                        SecretKey TEXT
                    )",

                    @"CREATE TABLE IF NOT EXISTS TerminalConfiguration (
                        Id INTEGER PRIMARY KEY CHECK (Id = 1),
                        TerminalId TEXT,
                        Label TEXT,
                        IsActive INTEGER,
                        Email TEXT,
                        Phone TEXT,
                        VersionNo INTEGER NOT NULL,
                        TradingName TEXT,
                        AddressLine TEXT
                    )",

                    @"CREATE TABLE IF NOT EXISTS OfflineLimit (
                        TerminalId TEXT PRIMARY KEY,
                        MaxTransactionAgeInHours INTEGER,
                        MaxCummulativeAmount REAL
                    )",

                    @"CREATE TABLE IF NOT EXISTS TaxpayerConfiguration (
                        TaxpayerId INTEGER PRIMARY KEY,
                        TIN TEXT UNIQUE NOT NULL,
                        IsVATRegistered INTEGER,
                        VersionNo INTEGER NOT NULL,
                        TaxOfficeCode TEXT
                    )",

                    @"CREATE TABLE IF NOT EXISTS TaxOffices (
                        Code TEXT PRIMARY KEY,
                        Name TEXT
                    )",

                    @"CREATE TABLE IF NOT EXISTS ActivationCode (
                        ActivationCode TEXT PRIMARY KEY
                    )",

                    @"CREATE TABLE IF NOT EXISTS GlobalConfiguration (
                        Id INTEGER PRIMARY KEY,
                        VersionNo INTEGER NOT NULL
                    )",

                    @"CREATE TABLE IF NOT EXISTS InvoiceTaxBreakDown (
                        InvoiceNumber TEXT,
                        RateID TEXT,
                        TaxableAmount REAL,
                        TaxAmount REAL
                    )",

                    @"CREATE TABLE IF NOT EXISTS HeldSales (
                        HoldId TEXT PRIMARY KEY,
                        CustomerName TEXT,
                        CustomerTIN TEXT,
                        CartDiscountAmount REAL,
                        CartDiscountPercent REAL,
                        HoldTime TEXT
                    )",

                    @"CREATE TABLE IF NOT EXISTS Customers (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL,
                        Phone TEXT,
                        Email TEXT,
                        Type TEXT,
                        TIN TEXT UNIQUE,
                        Address TEXT,
                        RegisteredDate TEXT DEFAULT (datetime('now')),
                        Notes TEXT
                    )",

                    @"CREATE TABLE IF NOT EXISTS HeldSaleItems (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        HoldId TEXT NOT NULL,
                        Barcode TEXT NOT NULL,
                        ProductName TEXT NOT NULL,
                        UnitPrice REAL NOT NULL,
                        Quantity REAL NOT NULL,
                        Discount REAL,
                        Total REAL,
                        TotalVAT REAL,
                        TaxRate TEXT,
                        UnitOfMeasure TEXT,
                        FOREIGN KEY(HoldId) REFERENCES HeldSales(HoldId)
                    )",

                    @"CREATE TABLE IF NOT EXISTS SecuritySettings (
                        ID INTEGER PRIMARY KEY CHECK (ID = 1),
                        RequireLogin BOOLEAN NOT NULL,
                        SessionTimeoutMinutes INTEGER NOT NULL,
                        EnableAuditLog BOOLEAN NOT NULL
                    )",

                    @"CREATE TABLE IF NOT EXISTS AuditLogs (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Username TEXT NOT NULL,
                        Action TEXT NOT NULL,
                        Details TEXT,
                        Timestamp TEXT NOT NULL
                    )",

                    @"CREATE TABLE IF NOT EXISTS ActivatedTaxRates (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        TaxType TEXT NOT NULL,
                        TaxpayerTin TEXT NOT NULL,
                        UNIQUE(TaxType, TaxpayerTin),
                        FOREIGN KEY(TaxpayerTin) REFERENCES TaxpayerConfiguration(TIN)
                    )",

                    @"CREATE TABLE IF NOT EXISTS Levies (
                        Id TEXT PRIMARY KEY,
                        Name TEXT NOT NULL,
                        ChargeMode TEXT NOT NULL,
                        Rate REAL NOT NULL,
                        IsActive INTEGER NOT NULL
                    )",

                    @"CREATE TABLE IF NOT EXISTS InvoiceLevies (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        InvoiceNumber TEXT NOT NULL,
                        LevyId TEXT NOT NULL,
                        LevyAmount REAL NOT NULL,
                        FOREIGN KEY(InvoiceNumber) REFERENCES Invoices(InvoiceNumber),
                        FOREIGN KEY(LevyId) REFERENCES Levies(Id)
                    )"
                };

                foreach (var sql in createStatements)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.ExecuteNonQuery();
                }

                Console.WriteLine($"All SQLite tables created and initialized at: {DbPath}");

                // TODO: mirrors Helper.insertDefaultAdminIfNotExists() from the Java version.
                // Share that Helper class if you want it ported too — it'd seed a default
                // ADMIN user into Users the first time the app runs.
            }
            catch (SqliteException ex)
            {
                Console.WriteLine($"Error initializing database: {ex.Message}");
            }
        }
    }
}