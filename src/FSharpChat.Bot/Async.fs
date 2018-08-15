namespace FSharpChat.Bot

[<AutoOpen>]
module Async = 
    let inline map (f: 'T -> 'TResult) (operation: Async<'T>) : Async<'TResult> = 
        async {
            let! result = operation
            return f result
        }

