module NotMyInbox.Messages

open SmtpServer
open SmtpServer.Storage
open SmtpServer.Protocol
open System.IO

// Implement a type that implements Mailbox
type MessageStore() =
    interface IMessageStore with
        member this.SaveAsync(context: ISessionContext, transaction: IMessageTransaction, buffer: System.Buffers.ReadOnlySequence<byte>, cancellationToken: System.Threading.CancellationToken): System.Threading.Tasks.Task<Protocol.SmtpResponse> = 
            task {
                use stream = new MemoryStream()
                let mutable position = buffer.Start
                let mutable memory = System.ReadOnlyMemory<byte>.Empty
                while buffer.TryGet(&position, &memory) do
                    stream.Write(memory.Span)
        
                stream.Position <- 0L

                let! message = (stream, cancellationToken) |> MimeKit.MimeMessage.LoadAsync
                let msg = message.ToString()

                return SmtpResponse(SmtpReplyCode.Ok, message.ToString())
            }
            

