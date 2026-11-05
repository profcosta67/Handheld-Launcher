namespace HandheldLauncher.App.Diagnostics;

/// <summary>
/// v0.16.2 — Central de Ajuda, etapa 2: manual de problemas e soluções.
/// Texto simples, embutido no app (funciona sem internet). Cada verificação
/// de <see cref="HealthChecks"/> pode apontar pra um artigo pelo Id.
///
/// Regra pra quem editar: só escrever o que é verdade no launcher. Problema
/// ainda em investigação é dito como tal — nada de solução inventada.
/// </summary>
internal static class HelpArticles
{
    public sealed record Article(string Id, string Category, string Title, string[] Paragraphs);

    public static readonly IReadOnlyList<Article> All = new Article[]
    {
        // ---------------- Controle ----------------
        new("controle-legion", "Controle", "O controle do Legion Go 2 não é reconhecido", new[]
        {
            "Às vezes, ao ligar o aparelho, o Windows não enxerga o controle embutido do Legion Go 2. É um problema conhecido do aparelho, em investigação no launcher.",
            "O que costuma resolver:",
            "1. Abrir o Legion Space — normalmente o controle volta logo depois. Pode fechá-lo em seguida.",
            "2. Desencaixar e encaixar de novo os controles.",
            "3. Reiniciar o aparelho.",
            "Ajude a investigar: quando acontecer, anote o horário e use Reportar erro (ou mande o debug.log). O launcher registra um \"retrato\" do controle ao abrir e sempre que ele conecta ou desconecta.",
            "Dica: a verificação \"Controle\" da Central de Ajuda mostra na hora se o Windows está vendo o controle.",
        }),
        new("qs-em-jogo", "Controle", "O Quick Settings não abre com o jogo aberto (L3+R3)", new[]
        {
            "Com um jogo rodando, quem detecta o L3+R3 é um processo auxiliar do launcher que lê o controle em segundo plano.",
            "Desde a 0.16.3 ele lê o controle por dois caminhos, incluindo o HID direto do controle — foi o que resolveu Days Gone e Pokémon pelo Eden, onde o Windows entregava o controle \"em repouso\" pro launcher.",
            "Alternativa que funciona com qualquer jogo: o atalho de teclado Ctrl+Alt+O abre e fecha o Quick Settings.",
            "Se mesmo assim não abrir em algum jogo, mande o log pelo Reportar erro: ele mostra qual caminho viu (ou não viu) o botão.",
        }),
        new("qs-navegacao", "Controle", "O Quick Settings aparece, mas o controle continua mexendo no launcher", new[]
        {
            "Esse problema (a tela do Quick Settings na frente, mas a navegação presa no launcher atrás) foi corrigido na v0.16.1.",
            "Se voltar a acontecer: aperte L3+R3 pra fechar e de novo pra abrir o Quick Settings. E reporte o erro, com o horário — não deveria mais acontecer.",
        }),

        // ---------------- Desempenho ----------------
        new("tdp-boot", "Desempenho", "O TDP não ficou como eu escolhi depois de ligar", new[]
        {
            "O launcher reaplica o último TDP escolhido logo depois de abrir. Em alguns boots, o firmware do aparelho reaplica os próprios limites logo em seguida e sobrescreve o do launcher.",
            "Desde a v0.16.2 o launcher tenta de novo sozinho (depois de 10s, 30s e 60s) e nunca passa por cima de um TDP que você tenha escolhido no Quick Settings nesse meio tempo.",
            "Pra conferir: a verificação \"Restauração da última sessão\" da Central de Ajuda mostra se o TDP, a ventoinha e a cor voltaram certo.",
            "Se ainda assim não ficar: abra o Quick Settings e escolha o TDP de novo.",
        }),
        new("tdp-stapm", "Desempenho", "O log fala em \"STAPM travado pelo EC\"", new[]
        {
            "É só um aviso. No Legion Go 2 o controlador da placa (EC) mantém o limite STAPM no valor dele, mas os limites que realmente valem no dia a dia (Fast PPT e Slow PPT) ficam no que você escolheu.",
            "Não precisa fazer nada.",
        }),

        // ---------------- Tela ----------------
        new("cor-tela", "Tela", "As cores da tela ficaram estranhas, fortes demais ou somando efeitos", new[]
        {
            "Até a v0.16.1 o launcher tinha dois sistemas de cor separados (um na abertura, outro no Quick Settings), e em alguns casos os efeitos se somavam ou a cor ficava presa depois de fechar o launcher. Corrigido na v0.16.2.",
            "Pra voltar ao normal: no Quick Settings, escolha a cor \"Padrão\". Fechar o launcher também restaura a cor original da tela.",
            "A luz noturna do Windows é outra coisa — ela é controlada nas configurações de vídeo do Windows, não pelo launcher.",
        }),

        // ---------------- Atualizações ----------------
        new("atualizacoes-beta", "Atualizações", "Como receber versões beta (ou voltar pra versão oficial)", new[]
        {
            "Em Configurações → Atualizações, marque ou desmarque \"Receber atualizações beta\" e clique em Salvar e fechar.",
            "A troca só vale depois de reiniciar o launcher.",
            "Atenção: quem está no canal beta não enxerga as versões oficiais, e vice-versa. Pra voltar ao oficial, desmarque, salve e reinicie — o launcher baixa a versão oficial mais recente.",
        }),
        new("atualizacoes-nao-aparece", "Atualizações", "A atualização não aparece", new[]
        {
            "O launcher procura atualização uns 20 segundos depois de abrir e depois a cada 6 horas. Dá pra procurar na hora em Menu → Atualizações → Verificar launcher.",
            "A verificação \"Atualizações\" da Central de Ajuda mostra a versão instalada, o canal (beta ou oficial) e o resultado da última procura — inclusive o erro, se houve.",
            "Se aparecer falha: confira a internet. Às vezes o GitHub limita as consultas por um tempo; tente mais tarde.",
        }),

        // ---------------- Configurações ----------------
        new("config-salvar", "Configurações", "Minhas configurações não salvaram", new[]
        {
            "Desde a v0.16.1 as Configurações salvam sempre que a janela fecha — pelo botão, pelo B do controle, pelo X da janela ou Alt+F4.",
            "Se algo ainda assim não ficar salvo, reporte o erro dizendo qual opção e como você fechou a janela.",
        }),
        new("modo-uso", "Configurações", "Modo Handheld × Uso em PC", new[]
        {
            "Handheld (recomendado no aparelho): tela cheia, abre sozinho no login e reabre se fechar.",
            "Uso em PC (Desktop): janela normal, não abre sozinho, e \"Fechar Launcher\" fecha de verdade.",
            "Troque em Configurações → Geral e salve — isso também liga ou desliga a reabertura automática do modo Handheld.",
            "Atenção: se o launcher foi instalado pelo instalador (Setup), ele também registra uma abertura no login que vale nos dois modos (tarefa \"HandheldLauncher_Startup\" no Agendador de Tarefas do Windows). A verificação \"Modo de uso\" da Central de Ajuda mostra se ela existe.",
            "Pra usar o Windows por um tempo: menu do launcher → Energia → Fechar Launcher. No modo Handheld, isso segura a reabertura automática até o próximo reinício do aparelho.",
            "Já Energia → Sair pro Desktop tira o launcher da tela cheia de vez (ele passa a abrir em janela). Pra voltar à tela cheia, escolha Handheld de novo em Configurações → Geral.",
        }),

        // ---------------- Biblioteca ----------------
        new("capas", "Biblioteca", "Os jogos estão sem capa", new[]
        {
            "As capas vêm do SteamGridDB e precisam de uma chave de API (grátis).",
            "1. Crie uma conta em steamgriddb.com e gere a chave nas preferências do perfil (seção API).",
            "2. Cole a chave em Configurações → Contas e salve.",
            "3. No menu do launcher → Biblioteca → Atualizar Capas Automático.",
        }),
        new("conta-steam", "Biblioteca", "Jogos da minha conta Steam não aparecem em \"Não instalados\" / Downloads", new[]
        {
            "Os jogos instalados aparecem sempre. Já a lista do que está na sua conta mas não instalado, os downloads e as conquistas precisam da chave da Steam Web API e do seu SteamID.",
            "Configure os dois em Configurações → Contas. O SteamID costuma ser preenchido sozinho com a conta da Steam do aparelho.",
            "O launcher nunca pede nem guarda a senha da Steam.",
        }),

        // ---------------- Lossless ----------------
        new("lossless", "Lossless Scaling", "Usar o Lossless Scaling pelo launcher", new[]
        {
            "O Lossless Scaling é vendido na Steam. Depois de instalado na pasta padrão, o launcher encontra sozinho; se estiver em outro lugar, indique o caminho em Configurações → Lossless.",
            "Com o jogo aberto, ative pelo Quick Settings → Lossless. Se o Lossless não estiver aberto, o launcher abre ele em segundo plano antes.",
            "Desde a 0.16.3 a janela principal do Lossless é minimizada sozinha e o Quick Settings volta pra direita se outro programa o mover. Se ainda aparecer tela cinza, mande o log pelo Reportar erro.",
        }),

        // ---------------- Emuladores ----------------
        new("eden", "Emuladores", "Os jogos do Eden não aparecem", new[]
        {
            "Em Configurações → Biblioteca, informe onde está o executável do Eden e as pastas dos jogos (ROMs), e salve.",
            "Se as ROMs estão num cartão de memória ou disco externo, ele precisa estar conectado.",
            "A verificação \"Emulador Eden\" da Central de Ajuda aponta se algum desses caminhos não existe mais.",
        }),

        // ---------------- Ajuda ----------------
        new("mandar-log", "Ajuda", "Como reportar um problema (e mandar o log)", new[]
        {
            "1. Anote o horário em que o problema aconteceu.",
            "2. Na Central de Ajuda, use \"Reportar erro\". O launcher salva um arquivo .zip na Área de Trabalho com o relatório das verificações, o log e as configurações — já sem chaves de API, SteamID, nome de usuário e nome do PC.",
            "3. Mande o .zip pelo grupo, ou use \"Abrir relato no GitHub\" (precisa de conta), descreva o problema e arraste o arquivo pra página.",
            "Nada é enviado sozinho: o pacote fica no aparelho até você mandar.",
        }),
    };

    public static Article? Find(string? id) => id is null ? null : All.FirstOrDefault(a => a.Id == id);
}
