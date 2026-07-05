using System.Threading.Tasks;
using FiscalReceiptParser.Data;
using Microsoft.Data.Sqlite;

namespace FiscalReceiptParser.Services
{
    public static class ProductInserter
    {
        /// <summary>
        /// Saves one product from GetTerminalSiteProductsAsync into the Products table.
        /// Ported from Java's Helper.insertOrUpdateProduct, matching the Products
        /// schema in Database.cs (ProductCode is the PRIMARY KEY, so this upserts).
        /// </summary>
        public static async Task InsertOrUpdateProductAsync(ProductsInventoryResponse product)
        {
            using var conn = Database.ConnOpen();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO Products
                    (ProductCode, ProductName, Description, Quantity, UnitOfMeasure,
                     Price, SiteId, ProductExpiryDate, MinimumStockLevel, TaxRateId,
                     IsProduct, Discount)
                VALUES
                    ($productCode, $productName, $description, $quantity, $unitOfMeasure,
                     $price, $siteId, $expiryDate, $minStock, $taxRateId,
                     $isProduct, $discount)";

            cmd.Parameters.AddWithValue("$productCode", product.ProductCode ?? "");
            cmd.Parameters.AddWithValue("$productName", product.ProductName ?? "");
            cmd.Parameters.AddWithValue("$description", product.Description ?? "");
            cmd.Parameters.AddWithValue("$quantity", product.Quantity);
            cmd.Parameters.AddWithValue("$unitOfMeasure", (object?)product.UnitOfMeasure ?? System.DBNull.Value);
            cmd.Parameters.AddWithValue("$price", product.Price.HasValue ? (object)product.Price.Value : System.DBNull.Value);
            cmd.Parameters.AddWithValue("$siteId", (object?)product.SiteId ?? System.DBNull.Value);
            cmd.Parameters.AddWithValue("$expiryDate",
                product.ProductExpiryDate.HasValue ? (object)product.ProductExpiryDate.Value.ToString("O") : System.DBNull.Value);
            cmd.Parameters.AddWithValue("$minStock", product.MinimumStockLevel.HasValue ? (object)product.MinimumStockLevel.Value : System.DBNull.Value);
            cmd.Parameters.AddWithValue("$taxRateId", (object?)product.TaxRateId ?? System.DBNull.Value);
            cmd.Parameters.AddWithValue("$isProduct", product.IsProduct ? 1 : 0);
            cmd.Parameters.AddWithValue("$discount", 0.0); // not present on this API response

            await cmd.ExecuteNonQueryAsync();
        }
    }
}