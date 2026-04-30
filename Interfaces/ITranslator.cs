namespace Sportive.API.Interfaces;

public interface ITranslator
{
    string Get(string key);
    string Get(string key, params object[] args);
}
