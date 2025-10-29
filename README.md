# 💸 SCAdvanced — Integração com Leitor de Cédulas MEI Advance SBN

> Biblioteca em **C# (.NET 8+)** para integração com validadores de cédulas compatíveis com o protocolo **EBDS (Enhanced Bill Dispenser/Stacker)**.  
> Desenvolvida para aplicações de **autoatendimento (kiosk)** com suporte a aceitação, devolução e controle de pagamento em espécie.

---

## 🧠 Visão Geral

O **SCAdvanced** permite comunicação direta com validadores da linha **MEI Advance (SBN / BNF)**, suportando leitura de notas em **modo Extended / PUP-A** e controle completo via porta serial (RS-232 ou USB-CDC).

A solução foi projetada com foco em **modularidade e reutilização**, separando responsabilidades entre camadas:
- **Core:** modelos, enums e contratos;
- **Service:** lógica EBDS e orquestração de pagamento;
- **ConsoleApp:** exemplo funcional e diagnóstico.

---

## ⚙️ Arquitetura
```
SCAdvanced
│
├── SCAdvanced.Core
│ ├── Contracts/ → Interfaces e contratos (ex: IBillAcceptorPort)
│ ├── Model/ → Entidades de domínio (Note, PaymentResult, etc.)
│ └── Model/Enum/ → Enumerações de estados e tipos (PaymentStatus, EbdsMessageType)
│
├── SCAdvanced.Service
│ ├── EbdsClient.cs → Comunicação EBDS via SerialPort
│ ├── BillAcceptorAdapter.cs → Abstração do hardware
│ └── PaymentOrchestrator.cs → Controle da sessão de pagamento
│
└── SCAdvanced.ConsoleApp
└── Program.cs → Exemplo prático de uso (autoatendimento)
```

---

## 🧩 Componentes Principais

### 🧠 EbdsClient
Gerencia a comunicação serial com o validador:
- Envio e leitura de frames EBDS (`Type1`, `Type2`, `Type6`, `Type7`);
- Configuração de modos (`Extended`, `Escrow`, `PUP-A`);
- Leitura da tabela de valores (`ValueTable`);
- Interpretação dos bytes EBDS em objetos de domínio (`NoteInfo`).

> 💡 Se o modelo **não suportar ESCROW**, o validador funcionará em modo *pass-through*, empilhando automaticamente as notas aceitas.

---

### 💸 PaymentOrchestrator
Controla o ciclo completo de uma operação de pagamento:
1. **Inicia a sessão** (`StartSession`);
2. **Monitora notas inseridas** (`WaitForEscrowNote`);
3. **Permite aceitar ou devolver** cada nota;
4. **Atualiza o valor pago** e dispara eventos;
5. **Finaliza a operação** (pagamento, cancelamento ou erro).

Eventos disponíveis:
- `Escrow` — nota detectada e aguardando decisão;  
- `Stacked` — nota aceita e empilhada;  
- `Completed` — sessão finalizada;  
- `Error` — erro de comunicação.

---

### 🧾 BillAcceptorAdapter
Camada intermediária entre `EbdsClient` e `PaymentOrchestrator`.

Responsável por:
- Traduzir comandos simples (`Stack()`, `Return()`, `Poll()`);
- Gerenciar bits de ACK/NAK do protocolo;
- Garantir consistência entre as trocas (handshakes).

---

## 🖥️ Exemplo de Uso (ConsoleApp)

```csharp
using SCAdvanced.Service;
using SCAdvanced.Core.Model;

Console.WriteLine("Iniciando leitor de cédulas...");
using var client = new EbdsClient("COM5");
client.Open();

client.LoadValueTable();
Console.WriteLine("Tabela de valores carregada com sucesso.");

var orchestrator = new PaymentOrchestrator(new BillAcceptorAdapter(client));

var options = new PaymentOptions
{
    AcceptedDenominations = new[] { 2, 5, 10, 20, 50 },
    InactivityTimeout = TimeSpan.FromSeconds(30),
    MaxChangeAllowed = 0,
    AllowChange = false
};

var result = orchestrator.StartSession(50m, options);

Console.WriteLine($"Status: {result.Status} | Valor pago: R$ {result.AmountPaid:N2}");
```

## 🔁 Fluxo de Pagamento
```
┌────────────────────────────┐
│ StartSession(total, opts)  │
└──────────────┬─────────────┘
               │
        ┌──────▼──────┐
        │ WaitForNote │
        └──────┬──────┘
               │
     ┌─────────┴─────────┐
     │                   │
 ┌───▼───┐           ┌───▼───┐
 │Return │           │ Stack │
 └───┬───┘           └───┬───┘
     │                   │
     └─────► Atualiza valor pago
               │
          ┌────▼────┐
          │ Pago?   │───Sim──► Encerrar
          └────┬────┘
               │
            Não│
               ▼
          Continua polling
```
## 🔍 Diagnóstico e Testes
# Verificar suporte a ESCROW
```csharp
var supportsEscrow = EbdsTestHelper.CheckEscrowSupport(client);
Console.WriteLine($"Suporta ESCROW: {supportsEscrow}");
```

# Consultar informações do validador
```csharp
Console.WriteLine($"Serial: {client.QuerySerialNumber()}");
Console.WriteLine($"Modelo: {client.QueryVariantName()}");
```

# Teste de inserção única
```csharp
if (client.WaitForNoteOnce(10000, out var note))
    Console.WriteLine($"Nota detectada: {note}");
else
    Console.WriteLine("Nenhuma nota detectada no tempo limite.");
```
## 🧰 Requisitos

.NET 8.0+

Porta Serial (RS-232) ou USB-CDC ativa (ex.: COM5)

Firmware compatível com EBDS (MEI Advance SBN)

Pacotes NuGet:

System.IO.Ports

Microsoft.Extensions.Logging

## 🚀 Execução

Conecte o validador à porta serial/USB;

Atualize o Program.cs com a porta correta (ex.: "COM5");

Execute:
```bash
dotnet run --project SCAdvanced.ConsoleApp
```

Insira cédulas quando solicitado.

## 📦 Estrutura Recomendada
| Pasta                   | Descrição                                       |
| ----------------------- | ----------------------------------------------- |
| `SCAdvanced.Core`       | Entidades e contratos independentes de hardware |
| `SCAdvanced.Service`    | Implementação EBDS real e orquestrador          |
| `SCAdvanced.ConsoleApp` | Exemplo de uso e diagnóstico                    |
