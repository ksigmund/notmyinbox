module MailClient

    open MailKit.Net.Smtp
    open MailKit.Security
    open MimeKit
    open MimeKit.Text

    let Message (fromOpt: string option,
                 toOpt: string option,
                 ccOpt: string option,
                 bccOpt: string option,
                 subjectOpt: string option,
                 textOpt: string option,
                 charset: string option,
                 body: MimeEntity option) =
        
        let message = new MimeMessage()

        message.From.Add(defaultArg fromOpt "from@sample.com" |> MailboxAddress.Parse)
        message.To.Add(defaultArg toOpt "to@sample.com" |> MailboxAddress.Parse)

        match ccOpt with
        | Some(cc) -> message.Cc.Add(MailboxAddress.Parse cc)
        | None -> ()

        match bccOpt with
        | Some(bcc) -> message.Bcc.Add(MailboxAddress.Parse bcc)
        | None -> ()

        message.Subject <- defaultArg subjectOpt "Hello"

        let mutable messageBody = defaultArg body None

        if messageBody = None then
            messageBody <- Some <| new TextPart(TextFormat.Plain)
            (messageBody.Value :?> TextPart).SetText(defaultArg charset "utf-8", defaultArg textOpt "Hello World")

        message.Body <- messageBody.Value

        message

    let Client (hostOpt: string option,
               portOpt: int option,
               optionsOpt: SecureSocketOptions option) =

        let client = new SmtpClient()
        
        client.Connected.Add(fun _ _ -> ())

        client.Connect(defaultArg hostOpt "localhost",
                       defaultArg portOpt 9025,
                       defaultArg optionsOpt SecureSocketOptions.Auto)

        client

    let Send (message: MimeMessage option,
              user: string,
              password: string) =
        
        let messageToSend = defaultArg message None
        let client = Client ()

        match user, password with
        | Some(u), Some(p) -> client.Authenticate(u, p)
        | _ -> ()

        client.Send(messageToSend)
        client.Disconnect(true)

    let NoOp (options: SecureSocketOptions option) =
        let client = Client ()
        client.NoOp()
