namespace BotService.Migrations

open System
open FluentMigrator

type MigrationAssemblyMarker = MigrationAssemblyMarker

[<AutoOpen>]
module MigrationConstants = 
    [<Literal>]
    let chatSchema = "fsharp_chat"

[<AutoOpen>]
module MigrationExtensions = 
    open FluentMigrator.Builders.Create.Table
    open FluentMigrator.Builders.Create
    open FluentMigrator.Builders.Delete

    type ICreateTableColumnAsTypeSyntax with
        member this.AsGuidPk() = 
            this.AsGuid().NotNullable().PrimaryKey()

    type ICreateExpressionRoot with
        member this.TableInChatSchema(tableName, (?description: string)) = 
            this.Table(tableName)
                .WithDescription(defaultArg description String.Empty)
                .InSchema(chatSchema)
    
    type IDeleteExpressionRoot with
        member this.TableInChatSchema(tableName) = 
            this.Table(tableName)
                .InSchema(chatSchema)

[<Migration(0L)>]
type CreateTablesMigration() = 
    inherit Migration()
    
    let usersTable = "users"

    override __.Up () = 
        __.Create.TableInChatSchema(usersTable)
            .WithColumn("id").AsGuidPk()
            .WithColumn("user_id").AsInt32().NotNullable()
            .WithColumn("username").AsString(50)
            .WithColumn("first_name").AsString(255)
            .WithColumn("last_name").AsString(255)
            .WithColumn("is_bot").AsBoolean().NotNullable().WithDefaultValue(false)
            |> ignore

    override __.Down () = 
        __.Delete.TableInChatSchema(usersTable)