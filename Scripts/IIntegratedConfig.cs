using IntegratedConfigs.Core.Entities;

namespace IntegratedConfigs.Scripts
{
    public interface IIntegratedConfig
    {
        XmlLoadInfo RegistrationInfo { get; }
    }
}