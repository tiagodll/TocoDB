open System
open System.IO
open System.Collections.Generic
open System.Text.RegularExpressions
open Argu
open Microsoft.Data.Sqlite

let fileNameRegex = new Regex("^(?<number>\\d*).*\\.sql$")

let parseInt (str:string) =
    match System.Int32.TryParse str with
    | true,int -> Some int
    | _ -> None
    
let checkIfTableExists table connection =
    try
        let command = new SqliteCommand($"SELECT name FROM sqlite_master WHERE type='table' AND name='{table}';", connection)
        let result = command.ExecuteScalar()
        result <> null
    with
        | :? Exception as ex ->
            Console.WriteLine $"unexpected ERROR {ex.Message}" |> ignore
            false
        
let createMigrationTable connection =
    let command = new SqliteCommand("CREATE TABLE IF NOT EXISTS migrations (version INT, timestamp DATETIME DEFAULT CURRENT_TIMESTAMP)", connection)
    command.ExecuteNonQuery() |> ignore
    let command2 = new SqliteCommand("INSERT INTO migrations (version) VALUES (0)", connection)
    command2.ExecuteNonQuery() |> ignore
    
let runMigration version sql connection =
    let command = new SqliteCommand(sql, connection)
    command.ExecuteNonQuery() |> ignore
    let command2 = new SqliteCommand($"UPDATE migrations SET version={version};", connection)
    command2.ExecuteNonQuery() |> ignore
    
let getCurrentVersion connection =
    let command = new SqliteCommand("SELECT version FROM migrations", connection)
    let result = command.ExecuteScalar()
    match result <> null with
    | false -> -1
    | true -> result.ToString() |> parseInt |> Option.defaultValue -1
    
let openOrCreate connectionString =
    let conn = new SqliteConnection (connectionString)
    conn.Open() |> ignore
    if false = checkIfTableExists "migrations" conn then
        createMigrationTable conn |> ignore
    conn
    
type MigrationFile = {
    version: int
    filepath: string
}

let migrationFileWithVersion (file:FileInfo) =
    let regx = fileNameRegex.Match(file.Name)
    match (regx.Success) with
    | false -> None
    | true ->
        let maybeVersion = regx.Groups.Item("number").Value |> parseInt
        match (maybeVersion.IsSome) with
            | false -> None
            | true -> Some {version=maybeVersion.Value; filepath=file.FullName}
            
let listMigrationFiles path currentVersion =
  let files = new List<MigrationFile>()
  let dir = new DirectoryInfo(path)
  for file in dir.GetFiles() do
    let mig = migrationFileWithVersion(file)
    if (mig.IsSome && mig.Value.version > currentVersion) then
        files.Add(mig.Value) |> ignore
  files

type Arguments =
    | [<Mandatory>][<AltCommandLine("-m")>] Migrations_Folder of migrationsPath:string
    | [<Mandatory>][<AltCommandLine("-c")>] Connection_String of connectionString:string
    
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Migrations_Folder _ -> "specify the directory where the migrations are contained."
            | Connection_String _ -> "specify a connection string to your database"

[<EntryPoint>]
let main(argv) =
    let errorHandler = ProcessExiter(colorizer = function ErrorCode.HelpText -> None | _ -> Some ConsoleColor.Red)
    let parser = ArgumentParser.Create<Arguments>(programName = "ls", errorHandler = errorHandler)

    let results = parser.ParseCommandLine argv

    let connection = openOrCreate (results.GetResult(Arguments.Connection_String))
    let version = getCurrentVersion connection
    let files = listMigrationFiles (results.GetResult(Arguments.Migrations_Folder)) version

    for file in files do
        let code = File.ReadAllText file.filepath
        runMigration file.version code connection |> ignore
        Console.WriteLine($"{file.filepath}")
    
    let versionfinal = getCurrentVersion connection
    Console.WriteLine($"database migrated to version: {versionfinal}")

    connection.Close();
    0