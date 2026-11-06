using System;
using System.Management;

namespace HandheldLauncher.App.Platform.Hardware;

public static class DeviceDetector
{
    public static DeviceProfile Detect()
    {
        try
        {
            // Lê o modelo no BIOS/WMI (ex: "83N1" para Legion Go 2)
            string model = GetWmiProperty("Win32_ComputerSystem", "Model")?.Trim().ToUpperInvariant() ?? "";
            string manufacturer = GetWmiProperty("Win32_ComputerSystem", "Manufacturer")?.Trim().ToUpperInvariant() ?? "";

            // 1. Legion Go 2 (8AHP2) - Validado
            if (model.Contains("83N1") || model.Contains("83N0"))
            {
                return new DeviceProfile
                {
                    Id = "legion-go-2",
                    Name = "Legion Go 2",
                    Manufacturer = "Lenovo",
                    TdpMin = 5, TdpMax = 35, TdpMaxAc = 35,
                    SupportsFanControl = true,
                    SupportsCustomTdp = true,
                    IsValidated = true
                };
            }
            
            // 2. Legion Go Original
            if (model.Contains("82CN") || model.Contains("83E1"))
            {
                return new DeviceProfile
                {
                    Id = "legion-go-1",
                    Name = "Legion Go",
                    Manufacturer = "Lenovo",
                    TdpMin = 5, TdpMax = 30, TdpMaxAc = 30,
                    SupportsFanControl = true,
                    SupportsCustomTdp = true,
                    IsValidated = true
                };
            }

            // 3. Legion Go S (PENDÊNCIA DO LOTE 1 RESOLVIDA AQUI!)
            if (model.Contains("83L3") || model.Contains("83Q2")) 
            {
                return new DeviceProfile
                {
                    Id = "legion-go-s",
                    Name = "Legion Go S",
                    Manufacturer = "Lenovo",
                    TdpMin = 5, TdpMax = 30, TdpMaxAc = 30,
                    SupportsFanControl = false, // Não validado
                    SupportsCustomTdp = true,
                    IsValidated = false,
                    ValidationMessage = "Aparelho não validado. TDP liberado com cautela; ventoinha desativada."
                };
            }

            // 4. ROG Ally / Ally X (Preparação para v0.19)
            if (model.Contains("RC71L") || model.Contains("RC72L"))
            {
                return new DeviceProfile
                {
                    Id = "rog-ally",
                    Name = "ROG Ally",
                    Manufacturer = "ASUS",
                    TdpMin = 5, TdpMax = 25, TdpMaxAc = 30, // Limites do Ally
                    SupportsFanControl = false,
                    SupportsCustomTdp = true,
                    IsValidated = false,
                    ValidationMessage = "Suporte ASUS em desenvolvimento. TDP básico liberado."
                };
            }

            // Fallback genérico
            return new DeviceProfile
            {
                Id = "generic",
                Name = "PC Genérico",
                Manufacturer = manufacturer,
                TdpMin = 5, TdpMax = 30, TdpMaxAc = 30,
                SupportsFanControl = false,
                SupportsCustomTdp = false,
                IsValidated = false,
                ValidationMessage = "Hardware não reconhecido. Recursos avançados desativados."
            };
        }
        catch (Exception ex)
        {
            return new DeviceProfile
            {
                Id = "unknown",
                Name = "Desconhecido",
                Manufacturer = "Desconhecido",
                IsValidated = false,
                ValidationMessage = "Falha ao ler hardware: " + ex.Message
            };
        }
    }

    private static string? GetWmiProperty(string wmiClass, string property)
    {
        using var searcher = new ManagementObjectSearcher($"SELECT {property} FROM {wmiClass}");
        foreach (var obj in searcher.Get())
        {
            return obj[property]?.ToString();
        }
        return null;
    }
}