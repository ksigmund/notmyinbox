module NotMyInBox.WorkerService

open Microsoft.Extensions.Hosting
open SmtpServer
open System.Threading

type Worker(smtpServer : SmtpServer) = 
    inherit BackgroundService()

    do
        System.Console.WriteLine "Starting BackgroundService"

    override this.ExecuteAsync (stoppingToken: CancellationToken) =
        task {
            do! smtpServer.StartAsync(stoppingToken)
        }
        

