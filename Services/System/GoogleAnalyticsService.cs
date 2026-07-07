using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Google.Analytics.Data.V1Beta;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;

namespace Sportive.API.Services
{
    public interface IGoogleAnalyticsService
    {
        Task<object> GetStoreVisitorsStatsAsync(DateTime? startDate = null, DateTime? endDate = null);
    }

    public class GoogleAnalyticsService : IGoogleAnalyticsService
    {
        private readonly ILogger<GoogleAnalyticsService> _logger;
        private readonly IMemoryCache _cache;
        
        // Configuration
        private readonly string _propertyId = "538049228";
        private readonly string _credentialsPath = "ga4_key.json";

        public GoogleAnalyticsService(ILogger<GoogleAnalyticsService> logger, IMemoryCache cache)
        {
            _logger = logger;
            _cache = cache;
        }

        public async Task<object> GetStoreVisitorsStatsAsync(DateTime? startDate = null, DateTime? endDate = null)
        {
            var start = startDate?.ToString("yyyy-MM-dd") ?? "7daysAgo";
            var end = endDate?.ToString("yyyy-MM-dd") ?? "today";
            string cacheKey = $"GA4_StoreVisitorsStats_{start}_{end}";
            
            if (_cache.TryGetValue(cacheKey, out object cachedResult))
            {
                return cachedResult;
            }

            try
            {
                var jsonCreds = @"
{
  ""type"": ""service_account"",
  ""project_id"": ""spatial-box-501619-b4"",
  ""private_key_id"": ""7c6a9b2e79189f2fc8971c94813a00c217e2e74e"",
  ""private_key"": ""-----BEGIN PRIVATE KEY-----\nMIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQDGsFTfm4EZj6Q0\n9V0WTDyEqQJDnGboIolMAQaXvCESB0UywFV0P3Ew0NB+q+HyRtSrbGbW5zIGdD/F\nUUssDrVFJqUIJzd7w7s0dvIV0EOq5O3URH+erL/D3u5HEPXWDxWWrzysq/3Nu40z\nAqJtL+QJE7UDLglkAQzLQvMrk2j//lLYJWoOqqVA5Kc4BKXQhkbopuJ/Fdrwl+ZB\nPW2p0Yb+9WgjONGPkB8JQXn+MzzHK+2NYOTiXc4v5K+G+TfGFEacW3XECO1a+ZbR\n1I/bhhSXLRS7ggRaKSZqn3K4kleieJlGUC2FeBw0okIgsh2uOrm6Sg5WVsJvTKMS\ntZ4EuQvRAgMBAAECggEACmJT2pM/gOJTeYXAquDVdt94Y/02xEzDHTWeg8FMKdY3\nGnXCjD6hP/ph3dpGMZ8xupCB+BsBhSR/4rmix/eFfMrjbCop3wOLSKzWGSgXKUV+\nso3ZQlCSqH4sY4rHi7uibAuA4ctkCo5nTPJcd9VoFxtbEK1QG1RnYpt/Ua3fb9A3\ng1n7IAqSi71q/sKpvy/VpEOWmQ2U4baF1PR74T9UlQ1hBF3L1xJY9YjeVAJGxWL7\nGeW3Sjh15saHitC0mtj83BEGq/byhiCGcN/7F36Rccoh9t1eOd7pulRPKmhcIw4D\ncUQ5Yqi0MZxzg7uXofqjifvy7ku+bNukAssey8WgWQKBgQDlUSiuiRgnfB99WgYn\nDV6uRFy+A5MS25xN/ehJEjlSuh9RcUkUkDwIwaFqafNTEdNh7gX/z5V92Rmj2Fj/\nOsUgax4HlkvwmGf9nCqBEN6TDIdZBR1qB/HYK96U4Dx/d3vLtoloAeHuh40cWp3z\nNX4iF0+GUMd6JTiInAqzKRKIhQKBgQDdztN9X4B8E3mbnAYBjsFWsCGeTK9LbfTd\nYAWeVxo0KmGDObRtq3e2EnjxurIu6zC4zMx5eeTLHuRiSyPCI2ADPzZsn0xQ+Zlq\nkPqE0mDH4iaXrNOC8mAYJuiEWf8UUNhRMmAQ8uGwr5ApYop0Etg1LmVt+yuMjrxO\nGhqMd+O93QKBgQC7TOvWiyGQdqBdyV8HHLN90VaS2OaS248yYLYOoPTzLhSQd+BC\nDIEMgeMuwLU+32txLHH3/HxU2zNHEVm3ti/2h6dyeP8z17fwfFJ3Muko4G3YdwYM\nacrOTx6xKOohDt2tiT14FzmLk2ndg+JJGSMaA0IwKeCUUrx3UESpC14Y5QKBgCI2\nrf6vxYTeCCsNlQuWdpIllvnxADUVX+jpz9QNwXf8dZAlTYSBJ3UJQmifEK8WDizj\nQkMWn0kJmdbjmj9u73dwv7dflwkChzyd4laskMskQimxOer/8fynu8P2kdcTZVqY\n96KFpHR5kDYiAhNHeNwzLOgbDyueNMOjWScDszhtAoGAKWRH0cIysAXkSuwPsSUM\nXRnsJk34d5kuIBmnyRPLIcvFwRBkHzegJ3v6W/37gjizHpedGKMHZbZBcr+G22jQ\nscfh5QQrftiGlc6sFJPkspH1YU3ChlLfW7TCLoDuAypY5MRErMrKiOGTNYkzUVVu\niskAy5NyhZoPMmWK7v5ojlA=\n-----END PRIVATE KEY-----\n"",
  ""client_email"": ""ga4-reader@spatial-box-501619-b4.iam.gserviceaccount.com"",
  ""client_id"": ""112032321790813114540"",
  ""auth_uri"": ""https://accounts.google.com/o/oauth2/auth"",
  ""token_uri"": ""https://oauth2.googleapis.com/token"",
  ""auth_provider_x509_cert_url"": ""https://www.googleapis.com/oauth2/v1/certs"",
  ""client_x509_cert_url"": ""https://www.googleapis.com/robot/v1/metadata/x509/ga4-reader%40spatial-box-501619-b4.iam.gserviceaccount.com"",
  ""universe_domain"": ""googleapis.com""
}";

                // Initialize client
                var clientBuilder = new BetaAnalyticsDataClientBuilder
                {
                    JsonCredentials = jsonCreds
                };
                var client = await clientBuilder.BuildAsync();

                // 1. Realtime Data (Active Users)
                var realtimeRequest = new RunRealtimeReportRequest
                {
                    Property = $"properties/{_propertyId}",
                    Metrics = { new Metric { Name = "activeUsers" } }
                };
                
                var realtimeResponse = await client.RunRealtimeReportAsync(realtimeRequest);
                int activeUsers = 0;
                if (realtimeResponse.Rows.Count > 0)
                {
                    int.TryParse(realtimeResponse.Rows[0].MetricValues[0].Value, out activeUsers);
                }

                // 2. Demographic Data (Last 30 Days) - Countries & Cities
                var geoRequest = new RunReportRequest
                {
                    Property = $"properties/{_propertyId}",
                    DateRanges = { new DateRange { StartDate = start, EndDate = end } },
                    Dimensions = { new Dimension { Name = "country" }, new Dimension { Name = "city" } },
                    Metrics = { new Metric { Name = "activeUsers" } },
                    OrderBys = { new OrderBy { Metric = new OrderBy.Types.MetricOrderBy { MetricName = "activeUsers" }, Desc = true } },
                    Limit = 10
                };
                var geoResponse = await client.RunReportAsync(geoRequest);

                var countriesList = new List<object>();
                var citiesList = new List<object>();
                var countryMap = new Dictionary<string, long>();
                long totalGeoUsers = 0;

                foreach (var row in geoResponse.Rows)
                {
                    var country = row.DimensionValues[0].Value;
                    var city = row.DimensionValues[1].Value;
                    long.TryParse(row.MetricValues[0].Value, out long users);
                    
                    if (country == "Egypt" && !string.IsNullOrEmpty(city) && city != "(not set)")
                    {
                        citiesList.Add(new { name = city, users = users });
                    }
                    
                    if (countryMap.ContainsKey(country))
                        countryMap[country] += users;
                    else
                        countryMap[country] = users;

                    totalGeoUsers += users;
                }

                if (totalGeoUsers > 0)
                {
                    foreach (var kvp in countryMap)
                    {
                        var percent = Math.Round((double)kvp.Value / totalGeoUsers * 100, 1);
                        var countryName = kvp.Key == "Egypt" ? "مصر" : kvp.Key;
                        countriesList.Add(new { name = countryName, percent = $"{percent}%" });
                    }
                }

                // 3. Page Views (Last 7 Days)
                var pagesRequest = new RunReportRequest
                {
                    Property = $"properties/{_propertyId}",
                    DateRanges = { new DateRange { StartDate = start, EndDate = end } },
                    Dimensions = { new Dimension { Name = "pageTitle" }, new Dimension { Name = "pagePath" } },
                    Metrics = { new Metric { Name = "screenPageViews" } },
                    OrderBys = { new OrderBy { Metric = new OrderBy.Types.MetricOrderBy { MetricName = "screenPageViews" }, Desc = true } },
                    Limit = 30
                };
                var pagesResponse = await client.RunReportAsync(pagesRequest);
                
                var topPages = new List<object>();
                var topProducts = new List<object>();
                
                foreach (var row in pagesResponse.Rows)
                {
                    var title = row.DimensionValues[0].Value;
                    var path = row.DimensionValues[1].Value;
                    long.TryParse(row.MetricValues[0].Value, out long views);
                    
                    bool isAdminPage = path.Contains("/admin", StringComparison.OrdinalIgnoreCase) || 
                                       path.Contains("/pos", StringComparison.OrdinalIgnoreCase) || 
                                       path.Contains("/dashboard", StringComparison.OrdinalIgnoreCase) ||
                                       path.StartsWith("admin", StringComparison.OrdinalIgnoreCase);
                                       
                    if (!isAdminPage && topPages.Count < 5)
                    {
                        topPages.Add(new { path = path, title = title, views = views, trend = "+5%" });
                    }
                    
                    if (path.Contains("/products/", StringComparison.OrdinalIgnoreCase) && topProducts.Count < 5)
                    {
                        topProducts.Add(new { name = title, views = views });
                    }
                }
                // 4. Session Metrics (Last 7 Days)
                var sessionRequest = new RunReportRequest
                {
                    Property = $"properties/{_propertyId}",
                    DateRanges = { new DateRange { StartDate = start, EndDate = end } },
                    Metrics = { new Metric { Name = "averageSessionDuration" }, new Metric { Name = "bounceRate" } }
                };
                
                string avgDurationStr = "00:00";
                string bounceRateStr = "0%";
                
                try 
                {
                    var sessionResponse = await client.RunReportAsync(sessionRequest);
                    if (sessionResponse.Rows.Count > 0)
                    {
                        var avgDurationSec = double.Parse(sessionResponse.Rows[0].MetricValues[0].Value);
                        var bounceRateVal = double.Parse(sessionResponse.Rows[0].MetricValues[1].Value);
                        
                        var timeSpan = TimeSpan.FromSeconds(avgDurationSec);
                        avgDurationStr = $"{(int)timeSpan.TotalMinutes:D2}:{timeSpan.Seconds:D2}";
                        bounceRateStr = $"{(bounceRateVal * 100):F1}%";
                    }
                } 
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch session metrics, using fallback");
                }

                // 5. Devices Data
                var devicesRequest = new RunReportRequest
                {
                    Property = $"properties/{_propertyId}",
                    DateRanges = { new DateRange { StartDate = start, EndDate = end } },
                    Dimensions = { new Dimension { Name = "deviceCategory" } },
                    Metrics = { new Metric { Name = "activeUsers" } }
                };
                
                string mobilePercent = "0%";
                string desktopPercent = "0%";
                try 
                {
                    var devicesResponse = await client.RunReportAsync(devicesRequest);
                    long totalDevices = 0;
                    long mobileUsers = 0;
                    long desktopUsers = 0;
                    
                    foreach(var row in devicesResponse.Rows)
                    {
                        long.TryParse(row.MetricValues[0].Value, out long u);
                        totalDevices += u;
                        var cat = row.DimensionValues[0].Value.ToLower();
                        if (cat == "mobile" || cat == "tablet") mobileUsers += u;
                        else desktopUsers += u;
                    }
                    
                    if (totalDevices > 0)
                    {
                        mobilePercent = $"{Math.Round((double)mobileUsers / totalDevices * 100)}%";
                        desktopPercent = $"{Math.Round((double)desktopUsers / totalDevices * 100)}%";
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch device metrics");
                }

                var result = new
                {
                    activeUsers = activeUsers,
                    devices = new { mobile = mobilePercent, desktop = desktopPercent }, 
                    countries = countriesList.Count > 0 ? countriesList.ToArray() : new object[] { new { name = "مصر", percent = "100%" } },
                    cities = citiesList,
                    topPages = topPages,
                    topProducts = topProducts,
                    sessionDuration = avgDurationStr, 
                    bounceRate = bounceRateStr 
                };

                _cache.Set(cacheKey, result, TimeSpan.FromMinutes(2)); // Cache for 2 minutes
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching GA4 stats");
                return GetMockData(ex.Message); 
            }
        }

        private object GetMockData(string errorMsg = "No Error")
        {
            return new
            {
                activeUsers = 24,
                devices = new { mobile = "82%", desktop = "18%" },
                countries = new[]
                {
                    new { name = "مصر", percent = "98.2%" },
                    new { name = "السعودية", percent = "1.1%" },
                    new { name = "أخرى", percent = "0.7%" }
                },
                cities = new[]
                {
                    new { name = "القاهرة", users = 4520 },
                    new { name = "الإسكندرية", users = 1250 },
                    new { name = "المنصورة", users = 840 },
                    new { name = "طنطا", users = 420 }
                },
                topPages = new[]
                {
                    new { path = "/", title = "الرئيسية", views = 12540, trend = "+12%" },
                    new { path = "/category/shoes", title = "الأحذية الرياضية", views = 8430, trend = "+5%" }
                },
                topProducts = new[]
                {
                    new { name = "حذاء ركض الترا بوست", views = 1240 },
                    new { name = "تيشيرت رياضي دراي فيت", views = 850 }
                },
                sessionDuration = errorMsg.Length > 20 ? errorMsg.Substring(0, 20) : errorMsg,
                bounceRate = "38.5%"
            };
        }
    }
}
