module NotMyInbox.Messages

open SmtpServer
open SmtpServer.Storage
open SmtpServer.Protocol
open System.IO
open Microsoft.Extensions.Caching.Memory
open SmtpServer.Mail
open System.Threading
open MimeKit
open MailKit.Net.Smtp
open System.Buffers
open System.Threading.Tasks
open System


// Function to convert a ReadOnlySequence<byte> to a MimeMessage
let convertBytesToMimeMessage (messageBytes: ReadOnlySequence<byte>) =
    // Convert ReadOnlySequence<byte> to a byte array
    let byteArray = messageBytes.ToArray()

    // Create a MemoryStream from the byte array
    use memoryStream = new MemoryStream(byteArray)

    // Load the MimeMessage from the MemoryStream
    MimeMessage.Load(memoryStream)

let sendEmail message smtpServer smtpPort (smtpUsername: string) (smtpPassword: string) =
    use client = new SmtpClient()

    task {
        client.Connect(smtpServer, smtpPort, useSsl = false) // Use SSL/TLS if required
        // client.Authenticate(smtpUsername, smtpPassword)
        let! result = client.SendAsync(message)
        client.Disconnect(true)
        return result
    }

// Implement a type that implements Mailbox
type MessageStore(cache: IMemoryCache) =
    interface IMessageStore with
        member _.SaveAsync
            (
                context: ISessionContext,
                transaction: IMessageTransaction,
                buffer: ReadOnlySequence<byte>,
                cancellationToken: CancellationToken
            ) : Task<Protocol.SmtpResponse> =
            task {
                let message = convertBytesToMimeMessage buffer

                let recipientAddress = message.To.Mailboxes |> Seq.head

                let cacheResult = cache.TryGetValue<string> recipientAddress.Address

                let msg = message.ToString() // just for debugging

                try
                    match cacheResult with
                    | (true, realAddress) ->
                        message.To.Clear()
                        message.To.Add(MailboxAddress(realAddress, realAddress))
                        cache.Remove(recipientAddress.Address)
                        let! z = sendEmail message "localhost" 9025 "blah" "blah"

                        return
                            Protocol.SmtpResponse(
                                SmtpReplyCode.Ok,
                                $"Forwarded email for {realAddress} to {recipientAddress}"
                            )
                    | (false, _) ->
                        return
                            Protocol.SmtpResponse(
                                SmtpReplyCode.BadEmailAddress,
                                $"No forwarding rule defined for {recipientAddress}"
                            )
                with ex ->
                    return Protocol.SmtpResponse(SmtpReplyCode.RelayDenied, $"Unable to forward email.")

            }

type MessageFilter(cache: IMemoryCache) =
    inherit MailboxFilter()

    override _.CanAcceptFromAsync
        (
            context: ISessionContext,
            from: IMailbox,
            size: int,
            cancellationToken: CancellationToken
        ) =
        task { return MailboxFilterResult.Yes }

    override _.CanDeliverToAsync
        (
            context: ISessionContext,
            toMailbox: IMailbox,
            fromMailbox: IMailbox,
            cancellationToken: CancellationToken
        ) =
        task {
            let keyExists, _ = cache.TryGetValue<string>(toMailbox.AsAddress())

            if (keyExists) then
                return MailboxFilterResult.Yes
            else
                return MailboxFilterResult.NoTemporarily
        }
