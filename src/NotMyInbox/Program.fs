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
open Microsoft.Extensions.Caching.Memory
open Messages
open Microsoft.Extensions.Configuration
open Azure.Communication.Email

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

    let emailPageView =
        [
          form
              [ _action "/fwd"; _method "POST" ]
              [ label [ _for "email" ] [ Text "Email Address:" ]
                input [ _type "email"; _id "from"; _name "From" ]
                button [ _type "submit" ] [ Text "Submit" ] ] ]
        |> layout

    let forwardingRuleCreatedPageView fromAddress toAddress =
        [ partial (); p [] [ 
            div [] [
                Text $"Forwarding rule created from "
                a [_href $"mailto:{toAddress}"] [Text toAddress]
                Text $"to {fromAddress}"]
         ] ] |> layout

    let index =
        [ partial (); p [] [ emailPageView ] ] |> layout



// ---------------------------------
// Web app
// ---------------------------------

let indexHandler =
    // let greetings = sprintf "Hello %s, from Giraffe!" name
    // let model = { Text = greetings }
    let view = Views.index
    htmlView view

let sendEmailHandler (recipient: string) : HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let client = new Net.Mail.SmtpClient("localhost", 9025)

            do! client.SendMailAsync("hello@amazon.com", recipient, "My Subject", "The body of the email!!!")

            return! text "Mail Sent" next ctx
        }

[<CLIMutable>]
type ForwardingRuleRequest = { From: string }

type ForwardingRule =
    { RealAddress: MailAddress
      FwdAddress: MailAddress }

let generateRandomEmailAddress (mailHost: string) =
    let random = Random()
    let minLength = 8
    let maxLength = 32
    let alphanumericChars = "abcdefghijklmnopqrstuvwxyz0123456789"

    let length = random.Next(minLength, maxLength + 1)

    let randomUsername =
        Array.init length (fun _ ->
            let randomCharIndex = random.Next(0, alphanumericChars.Length)
            alphanumericChars.[randomCharIndex])
        |> String

    new MailAddress($"{randomUsername}@{mailHost}")


let (|ValidForwardingRule|_|) (fwdRulesRequest: ForwardingRuleRequest) =
    let fromAddress = MailAddress.TryCreate(fwdRulesRequest.From)

    match fromAddress with
    | (true, from: MailAddress) ->
        Some
            { RealAddress = from
              FwdAddress = generateRandomEmailAddress "notmyinbox.com" }
    | _ -> None

let saveForwardingRule (cache: IMemoryCache) fwdRule =
    cache.Set(fwdRule.FwdAddress.Address, fwdRule.RealAddress.Address) |> ignore
    fwdRule.FwdAddress


let setupFwdHander: HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let! fwdRuleRequest = ctx.BindFormAsync<ForwardingRuleRequest>()
            let cache = ctx.GetService<IMemoryCache>()
            let result =
                match (fwdRuleRequest) with
                | ValidForwardingRule(fwdRule: ForwardingRule) ->
                    let fwdAddress = saveForwardingRule cache fwdRule
                    // Successful.created (json fwdAddress)
                    (Views.forwardingRuleCreatedPageView fwdRuleRequest.From fwdAddress.Address) |> htmlView 
                | _ -> RequestErrors.badRequest (text "Invalid email address")
            return! result next ctx
        }  

let webApp =
    choose [ 
        GET >=> choose [ 
            // routef "/fwd/%s" sendEmailHandler
            route "/" >=> indexHandler
        ] 
        POST >=> choose [
            route "/fwd" >=> setupFwdHander 
        ]
    ]

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

    services.AddTransient<IMessageStore, NotMyInbox.Messages.MessageStore>()
    |> ignore

    services.AddMemoryCache() |> ignore

    services.AddSingleton<SmtpServer>(fun (p: IServiceProvider) ->
        let options =
            (new SmtpServerOptionsBuilder())
                .ServerName("SMTP Server")
                .Port(25)
                .Port(587)
                .Port(465, true)
                .Port(9025)
                .Build()

        let server = new SmtpServer(options, p.GetRequiredService<IServiceProvider>())
        server)
    |> ignore

    services.AddTransient<EmailClient>(fun p ->
        let connectionString = Environment.GetEnvironmentVariable("ACS_CONNECTION_STRING")
        new EmailClient(connectionString))
    |> ignore

    services.AddTransient<IMailboxFilter, MessageFilter>() |> ignore

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
