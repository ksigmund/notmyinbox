module NotMyInbox.App

open System
open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Cors.Infrastructure
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.DependencyInjection
open Giraffe
open SmtpServer
open SmtpServer.Storage
open Microsoft.AspNetCore.Http
open NotMyInBox.WorkerService
open System.Net.Mail

// ---------------------------------
// Models
// ---------------------------------

type Message = { Text: string }

// ---------------------------------
// Views
// ---------------------------------

module Views =
    open Giraffe.ViewEngine

    let layout (content: XmlNode list) =
        html
            []
            [ head
                  []
                  [ title [] [ encodedText "NotMyInbox" ]
                    link [ _rel "stylesheet"; _type "text/css"; _href "/main.css" ] ]
              body [] content ]

    let partial () = h1 [] [ encodedText "NotMyInbox" ]

    let index (model: Message) =
        [ partial (); p [] [ encodedText model.Text ] ] |> layout

// ---------------------------------
// Web app
// ---------------------------------

let indexHandler (name: string) =
    let greetings = sprintf "Hello %s, from Giraffe!" name
    let model = { Text = greetings }
    let view = Views.index model
    htmlView view

let sendEmail : HttpHandler =
    fun (next : HttpFunc) (ctx : HttpContext) ->
        task {
            let client = new Net.Mail.SmtpClient("localhost", 9025)
            do! client.SendMailAsync("hello@amazon.com", "ksigmund@microsoft.com", "My Subject", "The body of the email!!!")
            return! text "Mail Sent" next ctx
        }

[<CLIMutable>]
type ForwardingRuleRequest = { From : string; To : string }

type ForwardingRule = { From : MailAddress; To: MailAddress }

let (|ValidForwardingRule|_|) (fwdRulesRequest : ForwardingRuleRequest) =
    let fromAddress = MailAddress.TryCreate(fwdRulesRequest.From)
    let toAddress = MailAddress.TryCreate(fwdRulesRequest.To)
    match fromAddress, toAddress with
        | (true, from: MailAddress), (true, ``to``: MailAddress) -> Some { From = from; To = ``to``}
        | _ -> None

let setupFwdHander (fwdRuleRequest : ForwardingRuleRequest) : HttpHandler =
    match (fwdRuleRequest) with
    | ValidForwardingRule (fwdRule: ForwardingRule)-> Successful.created (text $"Forwarding rule created from {fwdRule.From} to {fwdRule.To}")
    | _ -> RequestErrors.badRequest (text "Invalid email address")

let webApp =
    choose [
        GET >=> choose [
            routeBind<ForwardingRuleRequest> "/mail/{from}/to/{to}" setupFwdHander
            route "/mail" >=> sendEmail
        ]
        
    ]
    // choose [ 
    //     subRoute "/mail" (
    //         choose [
    //             GET >=> choose [
    //                 route "/" >=> sendEmail// make this POST
    //                 routeBind<ForwardingRule> "/%s/to/%s" setupFwdHander // POST eventually
    //             ]
    //         ]
    //     )
    //     GET  >=> choose [ 
    //         route "/" >=> indexHandler "world"; 
    //         routef "/hello/%s" indexHandler;
    //         setStatusCode 404 >=> text "Not Found"]
    //     POST >=> choose [
    //         route "/email" 
    //     ]
    // ]
          

// ---------------------------------
// Error handler
// ---------------------------------

let errorHandler (ex: Exception) (logger: Microsoft.Extensions.Logging.ILogger) =
    logger.LogError(ex, "An unhandled exception has occurred while executing the request.")
    clearResponse >=> setStatusCode 500 >=> text ex.Message

// ---------------------------------
// Config and Main
// ---------------------------------

let configureCors (builder: CorsPolicyBuilder) =
    builder
        .WithOrigins("http://localhost:5000", "https://localhost:5001")
        .AllowAnyMethod()
        .AllowAnyHeader()
    |> ignore

let configureApp (app: IApplicationBuilder) =
    let env = app.ApplicationServices.GetService<IWebHostEnvironment>()

    (match env.IsDevelopment() with
     | true -> app.UseDeveloperExceptionPage()
     | false -> app.UseGiraffeErrorHandler(errorHandler).UseHttpsRedirection())
        .UseCors(configureCors)
        .UseStaticFiles()
        .UseGiraffe(webApp)

let configureServices (services: IServiceCollection) =

    services.AddCors() |> ignore
    services.AddGiraffe() |> ignore
    services.AddTransient<IMessageStore, NotMyInbox.Messages.MessageStore>() |> ignore
    services.AddMemoryCache() |> ignore

    services.AddSingleton<SmtpServer>(fun (p: IServiceProvider) ->
        let options =
            (new SmtpServerOptionsBuilder())
                .ServerName("SMTP Server")
                .Port(9025)
                // .Port(465, true)
                .Build()

        let server = new SmtpServer(options, p.GetRequiredService<IServiceProvider>())
        server)
        |> ignore

    services.AddHostedService<Worker>() |> ignore


let configureLogging (builder: ILoggingBuilder) =
    builder.AddConsole().AddDebug() |> ignore

[<EntryPoint>]
let main args =
    let contentRoot = Directory.GetCurrentDirectory()
    let webRoot = Path.Combine(contentRoot, "WebRoot")

    Host
        .CreateDefaultBuilder(args)
        .ConfigureWebHostDefaults(fun webHostBuilder ->
            webHostBuilder
                .UseContentRoot(contentRoot)
                .UseWebRoot(webRoot)
                .Configure(Action<IApplicationBuilder> configureApp)
                .ConfigureServices(configureServices)
                .ConfigureLogging(configureLogging)
            |> ignore)
        .Build()
        .Run()

    0
