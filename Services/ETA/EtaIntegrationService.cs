using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Services.ETA;

public class EtaIntegrationService : IEtaIntegrationService
{
    private readonly AppDbContext _context;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<EtaIntegrationService> _logger;

    public EtaIntegrationService(AppDbContext context, IHttpClientFactory httpClientFactory, ILogger<EtaIntegrationService> logger)
    {
        _context = context;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string> UploadOrderToEtaAsync(int orderId)
    {
        var order = await _context.Orders
            .Include(o => o.Items)
            .Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
            throw new Exception("Order not found.");

        var storeInfo = await _context.StoreInfo.FirstOrDefaultAsync(s => s.StoreConfigId == 1);
        if (storeInfo == null || storeInfo.TaxAuthorityType != TaxAuthorityType.EgyptETA)
            throw new Exception("Store is not configured for ETA Integration.");

        if (string.IsNullOrEmpty(storeInfo.EtaClientId) || string.IsNullOrEmpty(storeInfo.EtaClientSecret))
            throw new Exception("ETA Credentials are missing in settings.");

        // 1. Get ETA Token
        var token = await GetEtaTokenAsync(storeInfo);

        // 2. Determine Document Type (B2B E-Invoice vs B2C E-Receipt)
        // Defaulting to B2C for Sportive (Retail)
        bool isB2C = true;
        
        string documentJson;
        if (isB2C)
        {
            documentJson = GenerateEReceiptJson(order, storeInfo);
            // B2C logic (e-Receipt)
        }
        else
        {
            documentJson = GenerateEInvoiceJson(order, storeInfo);
            // B2B logic (e-Invoice)
        }

        // 3. Sign Document (Local USB Token vs Cloud HSM)
        string signedDocumentJson;
        if (storeInfo.EtaSignatureType == "CloudHSM")
        {
            signedDocumentJson = await SignWithCloudHsmAsync(documentJson, storeInfo);
        }
        else
        {
            signedDocumentJson = await SignWithLocalAgentAsync(documentJson, storeInfo);
        }

        // 4. Submit to ETA
        var submissionResult = await SubmitToEtaAsync(signedDocumentJson, token, storeInfo, isB2C);

        // 5. Update Order Status
        order.TaxAuthorityStatus = "ETA_Submitted";
        order.TaxAuthorityQrCode = submissionResult; // Could be UUID or Receipt ID
        
        await _context.SaveChangesAsync();

        return submissionResult;
    }

    private async Task<string> GetEtaTokenAsync(StoreInfo store)
    {
        // Mock Implementation for ETA Token Retrieval
        // In reality, connects to https://id.eta.gov.eg/connect/token
        await Task.Delay(100);
        return "ETA_MOCK_TOKEN_" + Guid.NewGuid().ToString("N");
    }

    private string GenerateEInvoiceJson(Order order, StoreInfo store)
    {
        // Mock Implementation: Maps Order to ETA JSON Schema for B2B E-Invoice
        return "{\"issuer\":{\"id\":\"" + store.EtaTaxNumber + "\"},\"receiver\":{\"id\":\"" + order.CustomerId.ToString() + "\"},\"documentType\":\"I\"}";
    }

    private string GenerateEReceiptJson(Order order, StoreInfo store)
    {
        // Mock Implementation: Maps Order to ETA JSON Schema for B2C E-Receipt
        return "{\"header\":{\"posSerialNumber\":\"" + store.EtaPosSerial + "\"},\"documentType\":\"R\"}";
    }

    private async Task<string> SignWithLocalAgentAsync(string documentJson, StoreInfo store)
    {
        // Mock Implementation: Calls local signing agent (e.g., http://localhost:8080/sign)
        _logger.LogInformation("Signing document via Local USB Token Agent...");
        await Task.Delay(100);
        return documentJson.Replace("}", ",\"signatures\":[{\"signatureType\":\"I\",\"value\":\"LOCAL_AGENT_SIGN_MOCK\"}]}");
    }

    private async Task<string> SignWithCloudHsmAsync(string documentJson, StoreInfo store)
    {
        // Mock Implementation: Calls Egypt Trust / Misr Clearing Cloud HSM API
        _logger.LogInformation("Signing document via Cloud HSM...");
        await Task.Delay(100);
        return documentJson.Replace("}", ",\"signatures\":[{\"signatureType\":\"I\",\"value\":\"CLOUD_HSM_SIGN_MOCK\"}]}");
    }

    private async Task<string> SubmitToEtaAsync(string signedDocumentJson, string token, StoreInfo store, bool isB2C)
    {
        // Mock Implementation: Submits to ETA APIs
        // B2B: /api/v1/documentsubmissions
        // B2C: /api/v1/receiptsubmissions
        _logger.LogInformation($"Submitting {(isB2C ? "E-Receipt" : "E-Invoice")} to ETA...");
        await Task.Delay(200);
        return Guid.NewGuid().ToString("N"); // Return Mock UUID
    }
}
