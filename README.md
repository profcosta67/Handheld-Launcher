**Status Atual: v0.10.0 (O Marco do Linux)** — O launcher agora possui um motor multiplataforma (Core). A interface principal (Avalonia) compila nativamente para Windows e Linux (Steam Deck/Desktop). 

A versão 0.10.0 trouxe a substituição de bibliotecas exclusivas do Windows (como `System.Drawing`) pelo **SkiaSharp**, além de adaptar os leitores de hardware, permissões de arquivos e execução de jogos para o ecossistema Linux. A versão para Windows recebeu um novo sistema inteligente de cache de configurações, melhorando drasticamente a fluidez da interface, e ganhou proteções robustas contra arquivos de configuração corrompidos ou falhas nos scanners de lojas.

> **Nota sobre o suporte Linux:** O Core está 100% pronto e a interface compila. Funcionalidades específicas do Windows (como o HUD de FPS via RTSS, o teclado virtual nativo e leitura de GPU) estão desativadas no Linux, mas o launcher abre e gerencia a biblioteca Steam normalmente. 

Launcher "bigscreen" próprio, feito para rodar no **Legion Go 2**, **ROG Ally** e agora também no **Steam Deck** (Linux) — sem depender de nenhum launcher já existente.