using SCAdvanced.Core.Contracts;
using SCAdvanced.Core.Emun;
using SCAdvanced.Core.Model;
using SCAdvanced.Service;


TestePagamento();

void TestePagamento()
{
    using var cli = new EbdsClient();
    IBillAcceptorPort adapter = new BillAcceptorAdapter(cli);
    var orchestrator = new PaymentOrchestrator(adapter);

    Console.Write("Total (ex.: 37): ");
    if (!decimal.TryParse(Console.ReadLine(), out var total)) total = 37m;

    var opts = new PaymentOptions
    {
        ManualDecisionPerNote = true,      
        GivesChange = false,               
        AcceptedDenominations = new[] { 2, 5, 10, 20, 50, 100, 200 }
    };

    orchestrator.StartOperation(total, opts);
    Console.WriteLine("Insira cédulas. Use teclas: A=aceitar, R=devolver, F=Finalizar");

    while (true)
    {
        // 1) Espera chegar uma nota (ESCROW)
        var note = orchestrator.WaitForEscrowNote(timeoutMs: 1500);
        if (note != null)
        {
            Console.WriteLine($"> Nota detectada: BRL {note.Value:N2}. (A)citar ou (R)etornar?");
            ConsoleKey key;

            do { key = Console.ReadKey(true).Key; } while (key != ConsoleKey.A && key != ConsoleKey.R);

            var decision = key == ConsoleKey.A ? EscrowDecision.Accept : EscrowDecision.Return;
            var result = orchestrator.DecideOnEscrow(decision, confirmTimeoutMs: 4000);
            Console.WriteLine($"=> Resultado: {result.Status} | Valor: {result.Value:N2} {(result.ErrorCode != null ? $"({result.ErrorCode})" : "")}");
            note = null;
        }

        // 2) Comandos finais
        if (Console.KeyAvailable)
        {
            var k = Console.ReadKey(true).Key;
            if (k == ConsoleKey.F)
            {
                var res = orchestrator.ConfirmOperation();
                Console.WriteLine($"\nCONFIRMADO. Pago: {res.AmountPaid:N2}");
                break;
            }
        }
        Thread.Sleep(1000);
    }


    adapter.Dispose();
    Console.WriteLine("Fim.");
}
