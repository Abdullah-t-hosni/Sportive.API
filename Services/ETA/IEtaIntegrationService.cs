using System.Threading.Tasks;
using Sportive.API.Models;

namespace Sportive.API.Services.ETA;

public interface IEtaIntegrationService
{
    Task<string> UploadOrderToEtaAsync(int orderId);
}
