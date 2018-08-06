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
          
    type TelegramMessage = 
        | Text
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
            match message.Type with 
            | MessageType.Text -> 
                Text
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
        
module BotActors = 
    open Telegram 
    open Akkling  
    open Akkling.Actors
    open Akka.Routing
    open System.Threading.Tasks
    open Telegram.Bot.Types.Enums
           
    let private messageHandlerProps =
        fun (mailbox: Actor<_>) -> 
            let rec loop () = actor {
               let! message = mailbox.Receive()
               printfn "%s, %s" message (mailbox.Self.Path.ToStringWithUid())
               return! loop ()
            }
            loop ()
        |> props
        
    let botProps (configuration: BotConfiguration) =
        fun (mailbox: Actor<_>) ->  
            let bot = Telegram.createBot configuration
                               
            let messageHandlers = 
                let router = SmallestMailboxPool(10).WithResizer(DefaultResizer(1, 10)) :> RouterConfig
                spawn mailbox "message-handler" { messageHandlerProps with Router = Some router } 
                
            let handler = 
                fun args -> 
                    let message = TelegramMessage.parse args
                    match message with
                    | Text ->
                        messageHandlers <! "Text"
                    | _ -> 
                        ()
                                           
            let botHandler = 
                Task.Run(
                    fun _ ->
                        try 
                            bot.OnMessage |> Event.add (fun c -> printfn "%s" c.Message.Text )
                            let self = bot.GetMeAsync() |> Async.AwaitTask |> Async.RunSynchronously
                            printfn "%s" (self.Username);
                            bot.StartReceiving()                          
                        with 
                        | :? Exception as e ->
                            printfn "%s" e.Message             
                )          
        
            let rec loop () = actor {
               let! message = mailbox.Receive()
               return! loop ()
            }
            loop ()
        |> props