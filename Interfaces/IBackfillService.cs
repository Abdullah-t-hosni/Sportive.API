namespace Sportive.API.Interfaces;

public interface IBackfillService
{
    Task<(int Total, int Success, int Failed, List<string> Errors)> PostMissingOrdersAsync();
    Task<(int Total, int Success, int Failed, List<string> Errors)> PostMissingPurchasesAsync();
}
