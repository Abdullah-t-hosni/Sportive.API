using System;
using System.IO;
using Google.Analytics.Data.V1Beta;

class Program {
    static void Main() {
        try {
            var clientBuilder = new BetaAnalyticsDataClientBuilder { CredentialsPath = "ga4_key.json" };
            var client = clientBuilder.Build();
            var req = new RunRealtimeReportRequest { Property = "properties/538049228" };
            req.Metrics.Add(new Metric { Name = "activeUsers" });
            var res = client.RunRealtimeReport(req);
            Console.WriteLine("SUCCESS! Active users: " + res.Rows.Count);
        } catch (Exception ex) {
            Console.WriteLine("ERROR: " + ex.Message);
        }
    }
}
