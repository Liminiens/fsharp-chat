namespace FSharpChat.Bot

open System

module Telegram =
    open MihaZupan
    open System.Net
    open Telegram.Bot
    open Telegram.Bot.Args
    open Telegram.Bot.Types
    
    [<AllowNullLiteral>]
    type BotConfigurationJson() =
         member val Token = "" with get, set
         member val Socks5Host = "" with get, set
         member val Socks5Port = "" with get, set 
         member val Socks5Username = "" with get, set  
         member val Socks5Password = "" with get, set      
    
    type Socks5Configuration = { Host: string; Port: int; Username: string; Password: string }
    
    type BotConfiguration = { Token: string; Socks5Proxy: Socks5Configuration option}
    
    module BotConfiguration =
        open System.Reflection
        open System.IO
        open Microsoft.Extensions.Configuration
        
        let load =
            let isEmpty str = String.IsNullOrWhiteSpace(str)
                     
            let configuration = 
                let builder = 
                    ConfigurationBuilder()
                        .SetBasePath(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location))
                        .AddJsonFile("botconfig.json")
                        .Build()
                let settings = BotConfigurationJson()
                builder.GetSection("Settings").Bind(settings)
                settings |> Option.ofObj    
            
            let (|Valid|TokenEmpty|) token =
                if not <| isEmpty token then Valid else TokenEmpty
            
            let (|WithProxy|WithoutProxy|ProxyConfigurationError|) (host, port, username, password) = 
                let isHostEmpty = isEmpty host
                let isPortEmpty = isEmpty port
                let isUsernameEmpty = isEmpty username
                let isPasswordEmpty = isEmpty password
                
                let settings = [isHostEmpty; isPortEmpty; isUsernameEmpty; isPasswordEmpty]
                match settings |> List.forall (fun s -> s = false) with
                | true ->
                    WithProxy
                | false ->
                    if settings |> List.distinct |> List.length > 1 then 
                        ProxyConfigurationError else WithoutProxy
                                   
            match configuration with
            | Some(conf)  ->
                match conf.Token with
                | Valid ->
                    match conf.Socks5Host, conf.Socks5Port, conf.Socks5Username, conf.Socks5Password with
                    | WithProxy ->
                        let proxy = 
                            { Host = conf.Socks5Host; 
                              Port = int conf.Socks5Port; 
                              Username = conf.Socks5Username; 
                              Password = conf.Socks5Password }
                        { Token = conf.Token; Socks5Proxy = Some proxy } 
                        |> Ok
                    | WithoutProxy ->
                        { Token = conf.Token; Socks5Proxy = None }
                        |> Ok
                    | ProxyConfigurationError ->
                        Error "Error in proxy configuration"
                | TokenEmpty ->
                    Error "No bot token found"
            | None -> 
                Error "Configuration file parsing error"
                
    type Chat = { Id: int64; Title: string; Description: string; }
    
    type User = { Id: int32; Username: string; FirstName: string; LastName: string; }
    
    type Text = { Value: string; Date: DateTime; }
    
    type Audio = { Performer: option<string>; Title: option<string>;  MimeType: option<string>; File: byte[] }
    
    type MessageId = MessageId of int

    type ReplyToId = ReplyToId of int

    type MessageInfo = { MessageId: MessageId; ReplyToId: option<ReplyToId>; Forwarded: bool; Chat: Chat; User:User }

    type Message = 
        | TextMessage of MessageInfo * Text
        | AudioMessage of MessageInfo * Audio
        | Document
        | Video
        | Sticker
        | Photo
        | Voice
        | Skip
    
    module TelegramMessage =
        open Telegram.Bot.Types.Enums
        
        let parse (bot: TelegramBotClient) (messageArgs: MessageEventArgs) =
            let downloadFile fileId = 
                async {
                    use stream = new System.IO.MemoryStream()
                    do! bot.GetInfoAndDownloadFileAsync(fileId, stream) 
                        |> Async.AwaitTask
                        |> Async.Ignore
                    return stream.ToArray()
                }
            
            async {
                let message = messageArgs.Message
                
                let! messageInfo = 
                    async {
                        let! chat = 
                            async {
                                let! chatEntity = 
                                    bot.GetChatAsync(ChatId(message.Chat.Id)) 
                                    |> Async.AwaitTask

                                return 
                                    { Title = message.Chat.Title; 
                                      Id = message.Chat.Id;
                                      Description = chatEntity.Description }
                            }                    

                        let user = 
                            { Id = message.From.Id; 
                              Username = message.From.Username; 
                              FirstName = message.From.FirstName; 
                              LastName = message.From.LastName }
                
                        let replyToId = 
                            if isNotNull message.ReplyToMessage 
                                then Some(ReplyToId(message.ReplyToMessage.MessageId))
                                else None

                        return
                            { MessageId = MessageId(message.MessageId);
                              ReplyToId = replyToId;
                              Forwarded = isNotNull message.ForwardFrom
                              Chat = chat;
                              User = user }
                    }                

                match message.Type with 
                    | MessageType.Text -> 
                        let text = { Value = message.Text; Date = message.Date }
                        return TextMessage(messageInfo, text)
                    | MessageType.Audio ->
                        let! audioFile = downloadFile message.Audio.FileId
                        let audio = 
                            { Title = Option.ofObj message.Audio.Title; 
                              Performer = Option.ofObj message.Audio.Performer;
                              MimeType = Option.ofObj message.Audio.MimeType;
                              File = audioFile }
                        return AudioMessage(messageInfo, audio)
                    | MessageType.Document ->
                        return Document
                    | MessageType.Video ->
                        return Video
                    | MessageType.Sticker ->
                        return Sticker
                    | MessageType.Photo ->
                        return Photo
                    | MessageType.Voice ->
                        return Voice
                    | _ -> 
                        return Skip
            }     
            
    let createBot (configuration: BotConfiguration) =
        match configuration.Socks5Proxy with 
        | Some proxy ->
            let socksProxy = (HttpToSocks5Proxy(proxy.Host, proxy.Port, proxy.Username, proxy.Password) :> IWebProxy)
            TelegramBotClient(configuration.Token, socksProxy)
        | None ->
            TelegramBotClient(configuration.Token)
        
    module ActorProps = 
        open Akkling  
        open Akka.Routing
               
        let private messageMailboxProps =
            fun (mailbox: Actor<_>) -> 
                let rec loop () = actor {
                   let! message = mailbox.Receive()
                   match message with
                   | TextMessage(info, message) ->
                      printfn "%s %s %s" message.Value info.User.FirstName info.Chat.Title
                   | AudioMessage(info, message) ->
                      let title = match message.Title with | Some t -> t | None -> String.Empty
                      printfn "%s %i %s %s" title message.File.Length info.User.FirstName info.Chat.Title
                   | _ ->
                      ()
                   return! loop ()
                }
                loop ()
            |> props
        
        type BotMessage = 
            | PrintBotInfo of string    
            
        let botProps (configuration: BotConfiguration) =
            fun (mailbox: Actor<_>) ->                               
                let messageMailbox = 
                    let router = SmallestMailboxPool(10).WithResizer(DefaultResizer(1, 10, 3)) :> RouterConfig
                    spawn mailbox "new-message" { messageMailboxProps with Router = Some router } 
                    
                let bot = createBot configuration
                bot.OnMessage |> Event.add (fun args -> messageMailbox <!| (TelegramMessage.parse bot args))
                
                bot.GetMeAsync() 
                |> Async.AwaitTask 
                |> Async.Map (fun me -> PrintBotInfo(me.Username)) 
                |!> mailbox.Self
                
                let rec loop () = actor {
                   let! message = mailbox.Receive()
                                 
                   match message with 
                   | PrintBotInfo(username) ->
                       sprintf "Bot username is %s" username |> logInfo mailbox
                       bot.StartReceiving()
                       logInfo mailbox "Bot started receiving"
                        
                   return! loop ()
                }
                loop ()
            |> props