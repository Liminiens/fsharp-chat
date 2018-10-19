namespace BotService.AkkaExtensions
open Akkling
open Akka.Routing
open Akka.Actor

module Strategy = 
    let oneForOne (decider : exn -> Directive) : SupervisorStrategy = 
        upcast OneForOneStrategy(decider)

module Routing =
    let createConfig (config: 'TRouterConfig) = 
        config :> RouterConfig 

[<AutoOpen>]
module Props = 
    let propsRS recieve router strategy =
        { (props recieve) with Router = router; SupervisionStrategy = strategy }