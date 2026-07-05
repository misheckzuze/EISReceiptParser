using FiscalReceiptParser; // Points to your NSwag namespace
using Microsoft.VisualBasic.Logging;
using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Text.Json;
using FiscalReceiptParser.Models;
using System.Threading.Tasks;

namespace FiscalReceiptParser.Services
{
    public class MraApiService
    {
        private readonly HttpClient _httpClient;
        private const string MraBaseUrl = "https://dev-eis-api.mra.mw";

        public MraApiService(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        /// <summary>
        /// Activates the terminal using the exact NSwag toolchain models.
        /// </summary>
        public async Task<TerminalActivationResponseAPIResponse?> ActivateTerminalServiceAsync(string code)
        {
            // Instantiates the NSwag client using the layout you provided
            var generatedClient = new MraEisClient(MraBaseUrl, _httpClient);

            try
            {// 1. Map parameters using clean implicit target typing
                var activationPayload = new UnActivatedTerminal
                {
                    TerminalActivationCode = code,
                    Environment = new() // Let C# resolve the auto-generated environment type name automatically
                    {
                        Platform = new() // Let C# resolve the platform type name automatically
                        {
                            OsName = "Windows",
                            OsVersion = Environment.OSVersion.Version.ToString(),
                            OsBuild = Environment.OSVersion.VersionString,
                            MacAddress = GetMacAddress()
                        },
                        Pos = new() // Let C# resolve the POS configuration type name automatically
                        {
                            ProductID = "FiscalReceiptParser",
                            ProductVersion = "1.0.0"
                        }
                    }
                };
                // Log the exact outgoing payload before it's sent, so we can see whether
               // it's well-formed and matches what the MRA API expects field-for-field.
               string payloadJson = System.Text.Json.JsonSerializer.Serialize(activationPayload, new JsonSerializerOptions
               {
                 WriteIndented = true
             });
                System.Diagnostics.Debug.WriteLine("--- MRA ACTIVATION REQUEST PAYLOAD ---");
                System.Diagnostics.Debug.WriteLine(payloadJson);

                // 2. Invoke the exact NSwag client implementation method
                TerminalActivationResponseAPIResponse response = await generatedClient.ActivateTerminalAsync(activationPayload);

                // 3. Evaluate results based on your JSON schema fields mapping
                if (response != null && response.StatusCode == 1 && response.Data != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Terminal Activation Success: {response.Remark}");
                    System.Diagnostics.Debug.WriteLine(response.Data);

                    // Convert the response object back to a JSON string so your parser method can read it
                    string rawJson = System.Text.Json.JsonSerializer.Serialize(response);

                    bool isDbSaveSuccessful = await IsActivationSuccessfulAsync(rawJson, code);

                    if (!isDbSaveSuccessful)
                    {
                        System.Diagnostics.Debug.WriteLine("⚠️ API called succeeded, but local SQLite database tracking insertion failed.");
                    }
                    return response;
                }

                // Error validation fallback check logic
                if (response?.Errors != null && response.Errors.Any())
                {
                    var firstError = response.Errors.First();
                    throw new Exception($"[MRA Error {firstError.ErrorCode}] {firstError.ErrorMessage} (Field: {firstError.FieldName})");
                }

                throw new Exception(response?.Remark ?? "Unknown activation validation error from MRA server.");
            }
            catch (ApiException ex)
            {
                System.Diagnostics.Debug.WriteLine($"MRA API Exception Code: {ex.StatusCode}");
                System.Diagnostics.Debug.WriteLine($"MRA API Response Body: {ex.Response}");
                throw;
            }
        }

        private async Task<bool> IsActivationSuccessfulAsync(string responseBody, string code)
        {
            Console.WriteLine("Checking if activation was successful...");
            try
            {
                if (string.IsNullOrWhiteSpace(responseBody))
                {
                    Console.Error.WriteLine("Response body is null or empty");
                    return false;
                }

                // Parse to a light Document Object Model (DOM) to safely check layout keys
                using JsonDocument doc = JsonDocument.Parse(responseBody);
                JsonElement root = doc.RootElement;

                Console.WriteLine($"Parsed JSON response: {root}");

                if (root.TryGetProperty("StatusCode", out JsonElement statusCodeElement))
                {
                    int statusCode = statusCodeElement.GetInt32();
                    Console.WriteLine($"Found statusCode: {statusCode}");

                    if (statusCode == 1)
                    {
                        await ActivationDataInserter.InsertActivationDataAsync(responseBody);
                        await ActivationDataInserter.InsertActivationCodeAsync(code);

                        return true;
                    }
                    else
                    {
                        Console.Error.WriteLine($"Activation failed with statusCode: {statusCode}");
                    }
                }
                else
                {
                    Console.WriteLine("Response does not contain expected statusCode indicator");
                }
                return false;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Failed to parse response as JSON: {e.Message}");
                Console.Error.WriteLine($"Raw response: {responseBody}");
                return false;
            }
        }

        /// <summary>
        /// Confirms a terminal activation using the exact NSwag toolchain models.
        /// Mirrors ActivateTerminalServiceAsync's pattern.
        ///
        /// NOTE: TerminalActivatedConfirmationAsync takes x-signature as a parameter but has
        /// no bearer-token parameter — the generated code only adds the x-signature header
        /// itself. The JWT must therefore be set on _httpClient.DefaultRequestHeaders.Authorization
        /// before this call, since ActivateTerminalAsync (pre-activation) clearly needs no auth
        /// but this confirmation call logically requires the token this terminal was just issued.
        /// </summary>
        public async Task<bool> ConfirmActivationServiceAsync(string xSignature, string terminalId, string bearerToken)
        {
            var generatedClient = new MraEisClient(MraBaseUrl, _httpClient);

            try
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", bearerToken);

                var confirmPayload = new ActivatedTerminalConfirmation
                {
                    TerminalId = terminalId
                };

                string payloadJson = System.Text.Json.JsonSerializer.Serialize(confirmPayload, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                System.Diagnostics.Debug.WriteLine("--- MRA CONFIRM ACTIVATION REQUEST PAYLOAD ---");
                System.Diagnostics.Debug.WriteLine(payloadJson);
                System.Diagnostics.Debug.WriteLine($"x-signature: {xSignature}");

                BooleanAPIResponse response = await generatedClient.TerminalActivatedConfirmationAsync(xSignature, confirmPayload);

                if (response != null && response.StatusCode == 1)
                {
                    System.Diagnostics.Debug.WriteLine($"Terminal activation confirmed: {response.Remark}");

                    bool isActive = response.Data;
                    bool saved = ConfigHelper.UpdateIsActiveInTerminalConfiguration(isActive);

                    if (!saved)
                    {
                        System.Diagnostics.Debug.WriteLine("⚠️ Confirmation succeeded, but local SQLite update failed.");
                    }

                    return saved && isActive;
                }

                if (response?.Errors != null && response.Errors.Any())
                {
                    var firstError = response.Errors.First();
                    throw new Exception($"[MRA Error {firstError.ErrorCode}] {firstError.ErrorMessage} (Field: {firstError.FieldName})");
                }

                throw new Exception(response?.Remark ?? "Unknown confirmation error from MRA server.");
            }
            catch (ApiException ex)
            {
                System.Diagnostics.Debug.WriteLine($"MRA API Exception Code: {ex.StatusCode}");
                System.Diagnostics.Debug.WriteLine($"MRA API Response Body: {ex.Response}");
                throw;
            }
        }

        /// <summary>
        /// Orchestrates a confirmation call using values already persisted from the
        /// activation step. Ported from Java's confirmTerminalActivation().
        /// </summary>
        public async Task<bool> ConfirmTerminalActivationAsync()
        {
            var activationCode = ConfigHelper.GetActivationCode();
            var secretKey = ConfigHelper.GetSecretKey();
            var terminalId = ConfigHelper.GetTerminalId();
            var token = ConfigHelper.GetToken();

            if (string.IsNullOrEmpty(activationCode) || string.IsNullOrEmpty(secretKey) ||
                string.IsNullOrEmpty(terminalId) || string.IsNullOrEmpty(token))
            {
                System.Diagnostics.Debug.WriteLine("Cannot confirm activation — missing stored activation data.");
                return false;
            }

            var xSignature = XSignature.Compute(activationCode, secretKey);
            if (xSignature == null)
            {
                System.Diagnostics.Debug.WriteLine("Cannot confirm activation — failed to compute x-signature.");
                return false;
            }

            try
            {
                return await ConfirmActivationServiceAsync(xSignature, terminalId, token);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during terminal activation confirmation: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Fetches products for a terminal site and saves them to SQLite.
        /// Ported from Java's getTerminalSiteProducts. Matches the generated
        /// GetTerminalSiteProductsAsync(InventoryRequest) exactly.
        /// </summary>
        public async Task<bool> GetTerminalSiteProductsAsync(string tin, string siteId, string bearerToken)
        {
            var generatedClient = new MraEisClient(MraBaseUrl, _httpClient);

            try
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);

                var requestBody = new InventoryRequest
                {
                    Tin = tin,
                    SiteId = siteId
                };

                System.Diagnostics.Debug.WriteLine("--- FETCH TERMINAL SITE PRODUCTS REQUEST ---");
                System.Diagnostics.Debug.WriteLine(System.Text.Json.JsonSerializer.Serialize(requestBody));

                ProductsInventoryResponseListAPIResponse response =
                    await generatedClient.GetTerminalSiteProductsAsync(requestBody);

                System.Diagnostics.Debug.WriteLine($"Status Code: {response?.StatusCode}");

                if (response != null && response.StatusCode == 1 && response.Data != null)
                {
                    foreach (var product in response.Data)
                    {
                        await ProductInserter.InsertOrUpdateProductAsync(product);
                    }

                    System.Diagnostics.Debug.WriteLine("✅ Products saved to database.");
                    return true;
                }

                System.Diagnostics.Debug.WriteLine($"⚠ API Error: {response?.Remark}");
                return false;
            }
            catch (ApiException ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ HTTP Error {ex.StatusCode}");
                System.Diagnostics.Debug.WriteLine($"Response: {ex.Response}");
                return false;
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error fetching/saving products: {ex.Message}");
                return false;
            }
        }


/// <summary>
/// Submits a sales transaction. Ported from Java's submitTransactions.
/// The generated client's 400-response branch already parses the body into
/// ObjectAPIResponse for us via ApiException&lt;ObjectAPIResponse&gt;, so the
/// "Invoice Number already exists" (statusCode -2) case is read straight off
/// ex.Result rather than needing manual JSON parsing like the Java version did.
/// </summary>
public async Task<SubmitTransactionResult> SubmitSalesTransactionServiceAsync(SalesInvoice invoice, string bearerToken)
        {
            var generatedClient = new MraEisClient(MraBaseUrl, _httpClient);
            var result = new SubmitTransactionResult();

            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", bearerToken);

            System.Diagnostics.Debug.WriteLine("--- SUBMIT SALES TRANSACTION PAYLOAD ---");
            System.Diagnostics.Debug.WriteLine(System.Text.Json.JsonSerializer.Serialize(invoice, new JsonSerializerOptions { WriteIndented = true }));

            try
            {
                InvoiceResponseAPIResponse response = await generatedClient.SubmitSalesTransactionAsync(invoice);

                System.Diagnostics.Debug.WriteLine($"Status Code: {response?.StatusCode}, Remark: {response?.Remark}");

                if (response != null && response.StatusCode == 1)
                {
                    result.Success = true;
                    result.Remark = response.Remark ?? "";
                    if (response.Data != null)
                    {
                        result.ValidationUrl = response.Data.ValidationURL ?? "";
                        result.ShouldDownloadLatestConfig = response.Data.ShouldDownloadLatestConfig;
                    }
                    System.Diagnostics.Debug.WriteLine($"✅ Transactions submitted successfully: {result.Remark}");
                }
                else
                {
                    result.Remark = response?.Remark ?? "";
                    System.Diagnostics.Debug.WriteLine($"⚠️ Submission failed: {result.Remark}");
                }
            }
            catch (ApiException<ObjectAPIResponse> ex)
            {
                // This is the 400 Bad Request branch — includes the duplicate-invoice case.
                var body = ex.Result;
                result.Remark = body?.Remark ?? "";

                if (body != null && body.StatusCode == -2 &&
                    string.Equals(body.Remark, "Invoice Number already exists", System.StringComparison.OrdinalIgnoreCase))
                {
                    System.Diagnostics.Debug.WriteLine("ℹ️ Invoice already exists, marking as transmitted.");
                    result.Success = true;
                    result.WasDuplicate = true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Submission failed: {result.Remark}");
                }
            }
            catch (ApiException ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ HTTP error while submitting: {ex.StatusCode}");
                System.Diagnostics.Debug.WriteLine($"Response: {ex.Response}");
            }
            catch (System.Net.Http.HttpRequestException ex)
            {
                // Genuine network failure — no response at all. Java's catch-all "success = false"
                // covers this too; the caller treats it as "went offline", same outcome.
                System.Diagnostics.Debug.WriteLine($"❌ Network error during transaction submission: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Refreshes local config tables when the server signals ShouldDownloadLatestConfig.
        /// Reuses ActivationDataInserter.GetLatestConfigurationAsync, which expects camelCase
        /// keys — GetLatestConfigsAsync's Data is Newtonsoft-attributed with camelCase JSON
        /// names, so re-serializing with System.Text.Json's CamelCase policy reproduces that
        /// shape correctly for the existing parser to consume.
        /// </summary>
        public async Task FetchLatestConfigAsync(string bearerToken)
        {
            var generatedClient = new MraEisClient(MraBaseUrl, _httpClient);

            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", bearerToken);

            try
            {
                ConfigurationAPIResponse response = await generatedClient.GetLatestConfigsAsync();

                if (response?.Data != null)
                {
                    var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                    var wrapped = new { data = response.Data };
                    string json = JsonSerializer.Serialize(wrapped, jsonOptions);

                    await ActivationDataInserter.GetLatestConfigurationAsync(json);
                    System.Diagnostics.Debug.WriteLine("Latest config refreshed.");
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Failed to fetch latest config: {ex.Message}");
            }
        }
        /// <summary>
        /// Reads local hardware status for payload assignment matching.
        /// </summary>
        private string GetMacAddress()
        {
            try
            {
                var nic = NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(q => q.OperationalStatus == OperationalStatus.Up &&
                                         q.NetworkInterfaceType != NetworkInterfaceType.Loopback);

                return nic != null ? nic.GetPhysicalAddress().ToString() : "000000000000";
            }
            catch
            {
                return "000000000000";
            }
        }
    }
}