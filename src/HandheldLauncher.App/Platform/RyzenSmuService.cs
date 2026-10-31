using System;
using System.Threading;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

public sealed class RyzenSmuService : IDisposable
{
    private readonly PawnIOWrapper _io = new();
    private bool _init, _disposed;
    private readonly Mutex _smuMutex = new Mutex();

    public bool IsInitialized => _init;

    public bool Initialize(byte[] moduleData)
    {
        if (_init) return true;

        if (_io.Connect() != PawnIOWrapper.ConnectResult.OK || !_io.LoadModule(moduleData))
            return false;

        _init = true;
        return true;
    }

    // Envia um comando SMU. Para TDP, usamos "stapm-limit", "fast-limit", "slow-limit".
    public bool SendSmuCommand(string commandName, uint value)
    {
        if (!_init) return false;

        _smuMutex.WaitOne();
        try
        {
            // O PawnIO espera um array de ulongs com o comando.
            // O comando é: [endereço_SMU, valor_a_escrever]
            // Os endereços são específicos de cada família (Zen 2, 3, 4, etc.).
            // Aqui usamos um exemplo para Zen 4 (Phoenix/Strix Point). 
            // Para o seu Ryzen Z2 (Strix Point), os endereços estão na tabela do G-Helper.
            ulong smuAddress = commandName switch
            {
                "stapm-limit" => 0x14, // Exemplo: precisa ser confirmado para o Z2
                "fast-limit" => 0x15,
                "slow-limit" => 0x16,
                _ => 0xFFFF
            };

            if (smuAddress == 0xFFFF) return false;

            var input = new ulong[] { smuAddress, value };
            return _io.Execute("ioctl_send_smu_command", input, null);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[PawnIO] Erro ao enviar comando SMU '{commandName}': {ex.Message}");
            return false;
        }
        finally
        {
            _smuMutex.ReleaseMutex();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _init = false;
        _io.Dispose();
    }
}