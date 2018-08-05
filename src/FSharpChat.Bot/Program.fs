namespace FSharpChat.Bot    

open System
open Akkling 
open Telegram  

module BotActors = 
    open Akkling.Actors

    let messageHandlerProps =
        fun (mailbox: Actor<string>) -> 
            let rec loop () = actor {
               let! message = mailbox.Receive()
               printfn "%s, %s" message (mailbox.Self.Path.ToStringWithUid())
               return! loop ()
            }
            loop ()
        |> props
                          
module Main = 
    open Akka.Routing
    open Serilog
    open MihaZupan

    [<EntryPoint>]
    let main argv =
        Log.Logger <- Logger.setup    
           
        let configuration = BotConfiguration.load
        
        let config = Configuration.parse "akka { loglevel=INFO,  loggers=[\"Akka.Logger.Serilog.SerilogLogger, Akka.Logger.Serilog\"]}"       
        use system = System.create "telegram-bot" config       
        //let proxy = new HttpToSocks5Proxy("127.0.0.1", 1080);      
        let messageHandlers = 
            let router = SmallestMailboxPool(10).WithResizer(DefaultResizer(1, 10)) :> RouterConfig
            spawn system "message-handler" { BotActors.messageHandlerProps with Router = Some router }
        
        messageHandlers <! "Hi"
        
        Console.ReadKey() |> ignore
        0 // return an integer exit code
