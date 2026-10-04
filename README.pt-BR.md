# SNP Real Servers

[English](README.md) | [Português (Brasil)](README.pt-BR.md)

**Monte datacenters maiores com menos trabalho repetitivo.**

SNP Real Servers reúne três mods independentes feitos com MelonLoader para o jogo **Data Center, da Waseku**. O projeto adiciona perfis de hardware real ao catálogo de servidores, atribui IPs de clientes automaticamente e amplia a capacidade do carrinho para reduzir o número de viagens em instalações grandes.

Cada mod tem sua própria DLL. Você pode instalar apenas os recursos que quiser ou usar os três juntos.

## Os mods

| Mod | O que faz |
|---|---|
| **Real Servers** | Adiciona quatro opções de servidores Dell e Lenovo, com desempenho, perfis de rede, preços e tamanhos de rack próprios. Mantém a identidade do modelo de cada servidor entre salvamentos do jogo e suas revisões. |
| **Auto Customer DHCP** | Ao selecionar um cliente pelas setas da interface original do servidor, encontra um IP disponível em uma faixa compatível do cliente e o aplica pela configuração normal de IP do jogo. |
| **Infinite Trolley** | Adiciona espaços lógicos de carga conforme o carrinho enche. Os itens extras compartilham as posições visuais originais de carregamento, mas continuam sendo objetos separados que podem ser descarregados pelos controles normais. |

### Real Servers

A loja do mod tem quatro opções:

- Dell PowerEdge R440
- Dell PowerEdge R760
- Dell PowerEdge XE8640
- Servidor de rack Lenovo (nome exibido na loja: ThinkSystem SR630 V3)

O catálogo usa as interações originais dos servidores do jogo: instalação no rack, seleção de cliente, energia e controles de rede. A identidade do modelo acompanha os identificadores persistentes do servidor e do objeto, sem tentar deduzi-la pela cor Green/Yellow ou pela posição no rack. A limpeza da loja na versão **0.2.2** remove as opções extras sem modelo próprio; o catálogo original do jogo continua disponível.

**Este repositório inclui os quatro modelos de servidores usados no jogo, suas quatro versões leves para distância e os ícones disponíveis do R440 e do R760.** Os modelos detalhados aparecem de perto e as versões leves são usadas à distância. Projetos do Blender, modelos que não fazem parte do mod e arquivos pessoais de desenvolvimento ficam de fora. Os demais ícones podem ser adicionados conforme ficarem prontos.

### Auto Customer DHCP

A seleção do cliente aciona a atribuição de IP no quadro seguinte, depois que a interface original foi atualizada. O alocador preserva um endereço existente que seja válido, evita endereços ocupados e verifica a compatibilidade com o cliente, a aplicação e o tipo de servidor. Se não houver um endereço compatível disponível, informa o motivo e mantém o IP anterior.

Os endereços são salvos pelo sistema normal do jogo. Este mod funciona de forma independente e não exige o mod separado DHCP and IPAM.

### Infinite Trolley

O carrinho aumenta sua capacidade conforme necessário, sem um limite fixo de itens imposto pelo mod. As posições visuais sobrepostas evitam espalhar uma carga grande pelo cenário; o tratamento de colisões impede que os itens empilhados empurrem o carrinho. A memória disponível e o desempenho do jogo ainda determinam a capacidade prática.

A recuperação da carga usa os objetos restaurados pelo próprio jogo. Servidores nas suas mãos, instalados em racks ou pertencentes a outro recipiente ficam separados da recuperação do carrinho.

## Requisitos

- Windows x64 e uma instalação local de Data Center.
- MelonLoader **0.7.2**, com os assemblies IL2CPP gerados por ele.
- SDK do .NET **6** para compilar o código.

Os mods foram desenvolvidos para Unity **6000.4.12f1** e a API IL2CPP da instalação do jogo. Outras versões do jogo ou do carregador podem exigir adaptações nos pontos de integração e nas referências.

As DLLs do jogo, Unity, MelonLoader e de interoperabilidade geradas são obtidas da sua própria instalação. Elas não são redistribuídas neste repositório.

## Compilar

Clone ou baixe o repositório e informe a pasta do jogo:

```powershell
.\Build.ps1 -GameDir "C:\Program Files (x86)\Steam\steamapps\common\Data Center"
```

Ou compile a solução diretamente:

```powershell
dotnet build .\SNP.RealServers.sln -c Release '-p:GameDir=C:\Program Files (x86)\Steam\steamapps\common\Data Center'
```

Para compilar várias vezes, você pode definir `SNP_DATACENTER_DIR` com o caminho da pasta do jogo. Um `-p:GameDir=...` informado explicitamente tem prioridade. A compilação verifica as referências locais e informa as dependências ausentes antes de compilar.

| Arquivo gerado | Local |
|---|---|
| RealServers.dll | `RealServers/bin/Release/netcoreapp6.0/` |
| AutoCustomerDhcp.dll | `AutoCustomerDhcp/bin/Release/net6.0/` |
| InfiniteTrolley.dll | `InfiniteTrolley/bin/Release/net6.0/` |

## Instalar

Com o jogo fechado, execute os instaladores dos mods que quiser usar:

```powershell
$gameDir = "C:\Program Files (x86)\Steam\steamapps\common\Data Center"
.\RealServers\Install.ps1 -GameDir $gameDir
.\AutoCustomerDhcp\Install.ps1 -GameDir $gameDir
.\InfiniteTrolley\Install.ps1 -GameDir $gameDir
```

Os scripts compilam usando essa instalação, fazem backup dos arquivos existentes dos mods, copiam cada mod para `Mods` e verificam os hashes dos arquivos copiados. Eles não fecham nem iniciam o jogo e não alteram os saves. `-WhatIf` permite conferir a instalação sem copiar arquivos. Reinicie o jogo por conta própria para carregar uma DLL nova.

Por padrão, Real Servers instala sua DLL e os dez arquivos de recursos incluídos no repositório. A estrutura está descrita em [RealServers/Assets](RealServers/Assets/README.md). Para usar outro pacote com essa mesma estrutura, informe a pasta raiz dele:

```powershell
.\RealServers\Install.ps1 -GameDir $gameDir -AssetSource "C:\MyAssetPacks\RealServers"
```

Somente os dez arquivos usados em execução e listados no manifesto de recursos são selecionados. Projetos do Blender e arquivos de desenvolvimento que não fazem parte do mod ficam fora do pacote público.

## Compatibilidade dos saves e remoção

O jogo continua responsável pelos saves originais. Real Servers também armazena as identidades dos modelos em `UserData/RealServers/SaveIdentities/`, dentro da pasta do jogo. **Guarde essa pasta junto com os saves do jogo ao fazer backup ou transferir um mundo.**

Se um save antigo já perdeu tanto a identidade do modelo quanto seu registro correspondente, o mod não consegue reconstruir com segurança o hardware original usando apenas a aparência Green/Yellow do jogo.

Para remover um mod, feche o jogo e retire sua DLL de `Mods`. Antes de remover Infinite Trolley, descarregue os itens que excederem a capacidade original do carrinho. Preserve a pasta de identidades do Real Servers se pretender voltar a usar esses mundos com o mod.

## Desenvolvimento e verificação

O código inclui testes de regressão para alocação de IPs, espaços e recuperação do carrinho, regras de física, identidade dos modelos, operações de salvamento, dados do catálogo e compatibilidade dos pontos de integração com o jogo.

Os testes dos utilitários que usam apenas código gerenciado podem ser executados sem o jogo:

```powershell
dotnet run --project .\AutoCustomerDhcp\Tests\Allocator.Tests.csproj -c Release
dotnet run --project .\InfiniteTrolley\Tests\Core\Core.Tests.csproj -c Release
```

As verificações de metadados do Real Servers precisam dos assemblies da sua instalação. Defina a pasta usada em execução, além da propriedade usada na compilação:

```powershell
$env:SNP_DATACENTER_DIR = $gameDir
dotnet run --project .\RealServers\Tests\SaveHooks\SaveHooks.Tests.csproj -c Release "-p:GameDir=$gameDir"
```

Os testes de código gerenciado e de metadados não substituem a verificação de compra, retirada do servidor, instalação no rack, conexão de cabos e salvamento/carregamento dentro do jogo em Unity. Real Servers **0.2.6** mantém as quatro opções da loja, registra os slots de montagem pelo mecanismo nativo de UID, restaura os modelos físicos independentemente da consulta de identidade e evita chamadas nativas por vértice na importação. A recuperação de racks antigos exige uma posição e rotação salvas que correspondam a um único slot livre. Seu ciclo completo de uso ainda precisa ser verificado dentro do jogo.

## Estrutura do repositório

```text
RealServers/            Catálogo de servidores, persistência da identidade e carregador visual opcional
AutoCustomerDhcp/       Alocação automática de IPs dos clientes
InfiniteTrolley/        Espaços expansíveis do carrinho, física e recuperação da carga
Build.ps1               Compila os três mods
Directory.Build.props   Configuração compartilhada da pasta local do jogo
Directory.Build.targets Validação dos assemblies locais
```

Somente os recursos de execução listados no manifesto estão incluídos. Modelos 3D que não fazem parte do mod, projetos do Blender, binários do jogo e de dependências, arquivos compilados, backups locais, saves, dumps de falhas e logs ficam de fora. Os mods instalados permanecem separados deste repositório.

Data Center e os nomes das marcas de hardware pertencem aos respectivos proprietários. Este é um projeto independente de mods, sem vínculo com a Waseku ou com os fabricantes de hardware.

[Repositório](https://github.com/lucaspacazza/SNP_Real-Servers)
