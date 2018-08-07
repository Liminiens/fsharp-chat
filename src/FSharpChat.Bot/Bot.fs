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
    
    type BotConfigurationError =
        | FileFormatError 
        | NoToken
        | ProxyConfigurationError
    
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
                        Error ProxyConfigurationError
                | TokenEmpty ->
                    Error NoToken
            | None -> 
                Error FileFormatError
                
    type Chat = { Id: int64; Title: string; }
    
    type User = { Id: int32; Username: string; FirstName: string; LastName: string; }
    
    type Text = { Message: string }
         
    type Message = 
        | Text of Chat * User * Text
        | Audio
        | Document
        | Video
        | Sticker
        | Photo
        | Voice
        | Skip
    
    module TelegramMessage =
        open Telegram.Bot.Types.Enums
        
        let parse (messageArgs: MessageEventArgs) =
            let message = messageArgs.Message
            let chat = { Title = message.Chat.Title; Id = message.Chat.Id }
            let user = 
                { Id = message.From.Id; 
                  Username = message.From.Username; 
                  FirstName = message.From.FirstName; 
                  LastName = message.From.LastName }
                  
            match message.Type with 
            | MessageType.Text -> 
                Text(chat, user, { Message = message.Text; })
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
               
        let private messageHandlerProps =
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
            
        let botProps (configuration: BotConfiguration) =
            fun (mailbox: Actor<_>) ->                               
                let messageHandlers = 
                    let router = SmallestMailboxPool(10).WithResizer(DefaultResizer(1, 2)) :> RouterConfig
                    spawn mailbox "message" { messageHandlerProps with Router = Some router } 
                    
                let bot = createBot configuration
                bot.OnMessage |> Event.add (fun args -> messageHandlers <! (TelegramMessage.parse args))
                bot.StartReceiving()
                
                let rec loop () = actor {
                   let! message = mailbox.Receive()
                   return! loop ()
                }
                loop ()
            |> props