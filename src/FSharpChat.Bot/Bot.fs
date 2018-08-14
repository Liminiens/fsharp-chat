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
                        { Token = conf.Token; Socks5Proxy = Some({ Host = conf.Socks5Host; Port = int conf.Socks5Port; Username = conf.Socks5Username; Password = conf.Socks5Password }) } 
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
    
    type TextMessage = { Message: string; Date: DateTime; }
         
    type Message = 
        | Text of Chat * User * TextMessage
        | Audio
        | Document
        | Video
        | Sticker
        | Photo
        | Voice
        | Skip
    
    module TelegramMessage =
        open Telegram.Bot.Types.Enums
        open FSharpx.Control
        
        let parse (bot: TelegramBotClient) (messageArgs: MessageEventArgs) =
            let message = messageArgs.Message
            
            let chatEntity = bot.GetChatAsync(ChatId(message.Chat.Id)) |> Async.AwaitTask |> Async.RunSynchronously 
            
            let chat = 
                { Title = message.Chat.Title; 
                  Id = message.Chat.Id;
                  Description = chatEntity.Description }
            let user = 
                { Id = message.From.Id; 
                  Username = message.From.Username; 
                  FirstName = message.From.FirstName; 
                  LastName = message.From.LastName }
                  
            match message.Type with 
            | MessageType.Text -> 
                Text(chat, user, { Message = message.Text; Date = message.Date })
            | MessageType.Audio -> 
                Audio
            | MessageType.Document ->
                Document
            | MessageType.Video ->
                Video
            | MessageType.Sticker ->
                Sticker
            | MessageType.Photo ->
                Photo
            | MessageType.Voice ->
                Voice
            | _ -> 
                Skip
            
    let createBot (configuration: BotConfiguration) =
        let bot = 
            match configuration.Socks5Proxy with 
            | Some proxy ->
                let socksProxy = (HttpToSocks5Proxy(proxy.Host, proxy.Port, proxy.Username, proxy.Password) :> IWebProxy)
                TelegramBotClient(configuration.Token, socksProxy)
            | None ->
                TelegramBotClient(configuration.Token)
        bot
        
    module ActorProps = 
        open Akkling  
        open Akkling.Actors
        open Akka.Routing
        open FSharpx.Control
               
        let private messageMailboxProps =
            fun (mailbox: Actor<_>) -> 
                let rec loop () = actor {
                   let! message = mailbox.Receive()
                   match message with
                   | Text(chat, user, message) ->
                      printfn "%s %s" message.Message chat.Title
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
                    let router = SmallestMailboxPool(10).WithResizer(DefaultResizer(1, 2)) :> RouterConfig
                    spawn mailbox "new-message" { messageMailboxProps with Router = Some router } 
                    
                let bot = createBot configuration
                bot.OnMessage |> Event.add (fun args -> messageMailbox <! (TelegramMessage.parse bot args))
                
                bot.GetMeAsync() 
                |> Async.AwaitTask 
                |> Async.map (fun me -> PrintBotInfo(me.Username)) 
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