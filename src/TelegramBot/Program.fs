namespace FSharpChat.Bot    

open System
open Akka.FSharp
open FSharpChat.Bot.Telegram  
                         
module Main = 
    open Serilog

    [<EntryPoint>]
    let main argv =
        Console.OutputEncoding <- System.Text.Encoding.UTF8;
        Log.Logger <- Logger.setup true   
           
        let configuration = BotConfiguration.load 
               
        match configuration with 
        | Ok botConfig ->
                                  
            let config = Configuration.parse "akka { loglevel=INFO,  loggers=[\"Akka.Logger.Serilog.SerilogLogger, Akka.Logger.Serilog\"]}"       
            use system = System.create "telegram-bot" config
            
            let botActor = spawn system "bot" (BotActors.bot botConfig)
            
            Console.In.ReadLineAsync().GetAwaiter().GetResult() |> ignore
            
        | Error error -> 
            Log.Logger.Error("Configuration error: {0}", error)
            
        0 // return an integer exit code
