namespace FSharpChat.Bot    

open System
open Akkling 
open Telegram  
                         
module Main = 
    open Serilog

    [<EntryPoint>]
    let main argv =
        Log.Logger <- Logger.setup    
           
        let configuration = BotConfiguration.load 
               
        match configuration with 
        | Ok botConfig ->
                                  
            let config = Configuration.parse "akka { loglevel=INFO,  loggers=[\"Akka.Logger.Serilog.SerilogLogger, Akka.Logger.Serilog\"]}"       
            use system = System.create "telegram-bot" config
            
            let botActor = spawn system "bot" (ActorProps.botProps botConfig)
            
            Console.In.ReadLineAsync().GetAwaiter().GetResult() |> ignore
            
        | Error err -> 
            printfn "%s" err
            
        0 // return an integer exit code
