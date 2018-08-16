namespace FSharpChat.Bot

[<AutoOpen>]
module Async = 
    let inline Map (f: 'T -> 'TResult) (operation: Async<'T>) : Async<'TResult> = 
        async {
            let! result = operation
            return f result
        }
