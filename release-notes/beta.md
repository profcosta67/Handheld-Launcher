## Handheld Launcher — Beta de isolamento do controle no Quick Settings

### Correção em teste
- O isolamento do controle via HidHide agora identifica os nós HID e USB associados ao mesmo VID/PID do controle Lenovo detectado. Isso cobre o caminho USB que alguns jogos acessam por XInput, além do nó HID.
- A inclusão e a remoção do executável do jogo na lista de bloqueio passam a ser síncronas. O launcher não deixa a operação correr em segundo plano enquanto o Quick Settings abre ou fecha, reduzindo a janela em que comandos podem escapar para o jogo.
- O log passa a registrar quando não foi possível identificar o executável do jogo ou inicializar o isolamento.

### Como validar
1. Confirme que **Isolar controle no Overlay (HidHide)** está ativado nas configurações.
2. Abra um jogo compatível com controle.
3. Abra o Quick Settings e teste D-pad/analógico, A e B.
4. Verifique se o jogo deixa de receber os comandos enquanto o Quick Settings está aberto.
5. Feche o Quick Settings e confirme que o controle voltou a funcionar no jogo.

### Limitações conhecidas
- Esta é uma beta de validação em hardware real. O comportamento depende do driver HidHide, do caminho de entrada usado pelo jogo e da enumeração do controle no Windows.
- Alguns dispositivos XInput podem continuar visíveis por limitações conhecidas do HidHide ou por caminhos Raw Input. Se isso ocorrer, envie o arquivo de log para análise.
- Wi-Fi, Bluetooth e modo avião ainda não fazem parte desta entrega; esta beta não deve ser entendida como implementação desses controles.
