namespace FSharpChat.Bot

open System

module Media = 
    open ImageMagick

    let getMimeType (data: byte[]) =
        try
            let formatInfo = 
                MagickFormatInfo.Create(MagickImageInfo(data).Format)
            Some(formatInfo.MimeType)
        with
        | :? Exception as e ->
            None

module Telegram =
    open MihaZupan
    open System.Net
    open Telegram.Bot
    open Telegram.Bot.Args
    open Telegram.Bot.Types
    open System.IO
    
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
             
    type ChatId = ChatId of int64
    
    type UserId = UserId of int

    type Chat = { Id: ChatId; Title: string; Description: string; }
    
    type User = { Id: UserId; Username: string; FirstName: string; LastName: string; }
    
    type File = { MimeType: option<string>; Content: byte[] }
      
    type MessageId = MessageId of int

    type ReplyToId = ReplyToId of int

    type Text = { Value: string; Date: DateTime; }
    
    type Sticker = { Emoji: string; PackName: option<string>; Thumb: File; Sticker: File }
    
    type Audio = { Performer: option<string>; Title: option<string>; File: File; Caption: option<string> }

    type Video = { File: File; Thumb: option<File>; Caption: option<string> }

    type Document = { FileName: string; File: File; Thumb: option<File>; Caption: option<string> }

    type MessageInfo = { MessageId: MessageId; ReplyToId: option<ReplyToId>; Forwarded: bool; Chat: Chat; User:User }

    type Message = 
        | TextMessage of MessageInfo * Text
        | AudioMessage of MessageInfo * Audio
        | Document of MessageInfo * Document
        | Video of MessageInfo * Video
        | StickerMessage of MessageInfo * Sticker
        | Photo
        | Voice
        | Skip
    
    module TelegramMessage =
        open Telegram.Bot.Types.Enums
        open Akka.FSharp
        
        let parse (bot: TelegramBotClient) (messageArgs: MessageEventArgs) =
            let downloadFile fileId = 
                async {
                    let stream = new System.IO.MemoryStream()
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
                                    bot.GetChatAsync(Telegram.Bot.Types.ChatId(message.Chat.Id)) 
                                    |> Async.AwaitTask

                                return 
                                    { Title = message.Chat.Title; 
                                      Id = ChatId(message.Chat.Id);
                                      Description = chatEntity.Description }
                            }                    

                        let user = 
                            { Id = UserId(message.From.Id); 
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
                              File = { MimeType = Option.ofObj message.Audio.MimeType; Content = audioFile };
                              Caption = Option.ofObj message.Caption }
                        return AudioMessage(messageInfo, audio)
                    | MessageType.Document ->
                        let! documentFile = downloadFile message.Document.FileId
                        let! thumb = 
                            match isNotNull message.Document.Thumb with
                            | true ->
                                async {        
                                    let! thumbFile = downloadFile message.Document.Thumb.FileId
                                    return
                                        { MimeType = Media.getMimeType thumbFile; Content = thumbFile } 
                                        |> Some
                                }
                            | false -> 
                                Async.AsAsync None
                        let document = 
                            { FileName = message.Document.FileName;
                              Thumb = thumb;
                              File = { MimeType = Option.ofObj message.Document.MimeType; Content = documentFile };
                              Caption = Option.ofObj message.Caption }

                        return Document(messageInfo, document)
                    | MessageType.Video ->
                        let! videoFile = downloadFile message.Video.FileId
                        let! thumb = 
                            match isNotNull message.Video.Thumb with
                            | true ->
                                async {        
                                    let! thumbFile = downloadFile message.Video.Thumb.FileId
                                    return
                                        { MimeType = Media.getMimeType thumbFile; Content = thumbFile } 
                                        |> Some
                                }
                            | false -> 
                                Async.AsAsync None
                            
                        let video = 
                            { Thumb = thumb; 
                              File = { MimeType = Media.getMimeType videoFile; Content = videoFile };
                              Caption = Option.ofObj message.Caption  }
                        return Video(messageInfo, video)
                    | MessageType.Sticker ->
                        let! [|thumbFile; stickerFile|] = 
                            [downloadFile message.Sticker.Thumb.FileId; downloadFile message.Sticker.FileId]
                            |> Async.Parallel
                            
                        let stickerInfo = 
                            { Emoji = message.Sticker.Emoji;
                              PackName = Option.ofObj message.Sticker.SetName;
                              Thumb = { MimeType = Media.getMimeType thumbFile; Content = thumbFile };
                              Sticker = { MimeType = Media.getMimeType stickerFile; Content = stickerFile } }
                        return StickerMessage(messageInfo, stickerInfo)
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
        open Akka
        open Akka.FSharp
        open Akka.Routing
        open Akka.Actor
               
        let private telegramMessageActor (mailbox: Actor<_>) =
            let rec loop () = actor {
                let! message = mailbox.Receive()
                match message with
                | TextMessage(info, message) ->
                    logInfo mailbox message.Value
                | AudioMessage(info, message) ->      
                    logInfo mailbox <| sprintf "Size: %i Name: %s" message.File.Content.Length (defaultArg message.Title "")
                | StickerMessage(info, message) ->
                    logInfo mailbox  <| sprintf "Emoji: %s; MimeType: %s" message.Emoji (defaultArg message.Sticker.MimeType "")
                | _ ->
                    ()
                return! loop ()
            }
            loop ()
        
        type BotMessage = 
            | BotAlive of username: string    
            
        let botActor (configuration: BotConfiguration) (mailbox: Actor<_>) =                           
            let messageActor = 
                let router = SmallestMailboxPool(10).WithResizer(DefaultResizer(1, 10, 3))
                let strategy =
                    fun (exc: Exception) ->
                        logError mailbox "Actor failed"
                        logException mailbox exc
                        Directive.Resume
                spawnOpt mailbox "new-message" telegramMessageActor 
                    [SpawnOption.Router(router); SpawnOption.SupervisorStrategy(Strategy.OneForOne(strategy))]
                    
            let bot = createBot configuration
            bot.OnMessage |> Event.add (fun args -> messageActor <!| (TelegramMessage.parse bot args))
                
            bot.GetMeAsync() 
            |> Async.AwaitTask 
            |> Async.Map (fun me -> BotAlive(me.Username)) 
            |!> mailbox.Self
                
            let rec loop () = actor {
                let! message = mailbox.Receive()
                                 
                match message with 
                | BotAlive(username) ->
                    logInfo mailbox <| sprintf "Bot username is %s" username
                    bot.StartReceiving()
                    logInfo mailbox "Bot started receiving"
                        
                return! loop ()
            }
            loop ()