using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using CommandLineParser.Arguments;
using CommandLineParser.Exceptions;
using NJsonSchema;
using NJsonSchema.CodeGeneration.CSharp;
using NJsonSchema.CodeGeneration.TypeScript;

namespace NJsonSchema.CodeGeneration.CLI.Console
{
    class Program
    {
        static async Task Main(string[] args)
        {

            var parser = new CommandLineParser.CommandLineParser()
            {
                CheckMandatoryArguments = true,
                ShowUsageOnEmptyCommandline = true
            };

            var schemaHttpArg = new ValueArgument<string>('r', "remote", "json schema source http address")
            {
                Optional = true
            };
            var namespaceArg = new ValueArgument<string>('n', "namespace", "Namespace of generated code")
            {
                Optional = true
            };
            var schemaDirArg = new DirectoryArgument('s', "schema", "json schema source directory")
            {
                Optional = true
            };
            var typeScriptDirArg = new DirectoryArgument('t', "typescript", "typescript target directory")
            {
                Optional = true,
                DirectoryMustExist = false
            };
            var cSharpDirArg = new DirectoryArgument('c', "csharp", "c# target directory")
            {
                Optional = true,
                DirectoryMustExist = false
            };
            
            parser.Arguments.Add(schemaDirArg);
            parser.Arguments.Add(typeScriptDirArg);
            parser.Arguments.Add(cSharpDirArg);
            parser.Arguments.Add(schemaHttpArg);
            parser.Arguments.Add(namespaceArg);

            try
            {
                parser.ParseCommandLine(args);
                if (!parser.ParsingSucceeded)
                    return;
            }
            catch (Exception e)
            {
                System.Console.WriteLine("Exception parsing arguments:");
                System.Console.WriteLine(e.Message);
                parser.PrintUsage(System.Console.Out);
                System.Console.WriteLine("press any key to continue");
                System.Console.ReadKey();
                return;
            }

            if (typeScriptDirArg.DirectoryInfo == null && 
                cSharpDirArg.DirectoryInfo == null)
            {
                System.Console.WriteLine("TypeScript and/or C# target directory missing");
                System.Console.WriteLine("press any key to continue");
                System.Console.ReadKey();
                return;
            }
            if (schemaDirArg.DirectoryInfo == null &&
                schemaHttpArg.Value == null)
            {
                System.Console.WriteLine("Either Source Url or Source Directory must be provided");
                System.Console.WriteLine("press any key to continue");
                System.Console.ReadKey();
                return;
            }

            DirectoryInfo sDirInfo = null;
            if (schemaHttpArg.Value != null)
            {
                if (!Directory.Exists("temp")) Directory.CreateDirectory("temp");

                var client = new HttpClient();
                var stream = await client.GetByteArrayAsync(schemaHttpArg.Value);
                await File.WriteAllBytesAsync("temp/target.json", stream);
                sDirInfo = new DirectoryInfo("temp");
            }
            else
            {
                sDirInfo = schemaDirArg.DirectoryInfo;
            }
            
            var tDirInfo = typeScriptDirArg.DirectoryInfo;
            var cDirInfo = cSharpDirArg.DirectoryInfo;
            if (tDirInfo != null && !tDirInfo.Exists)
            {
                tDirInfo.Create();
            }
            if (cDirInfo != null && !cDirInfo.Exists)
            {
                cDirInfo.Create();
            }

            await GenCodeForFolder(sDirInfo, tDirInfo, cDirInfo, namespaceArg.Value);

            var schemaFolders = sDirInfo.GetDirectories().ToList();
            foreach (var folder in schemaFolders)
            {
                //tDirInfo
                DirectoryInfo tsDir = null;
                if (tDirInfo != null)
                {
                    if (!Directory.Exists(tDirInfo.FullName + @"\" + folder.Name))
                        Directory.CreateDirectory(tDirInfo.FullName + @"\" + folder.Name);

                    tsDir = new DirectoryInfo(tDirInfo.FullName + @"\" + folder.Name);
                }

                DirectoryInfo csDir = null;
                if (cDirInfo != null)
                {
                    System.Console.WriteLine(cDirInfo.FullName + @"\" + folder.Name);
                    if (!Directory.Exists(cDirInfo.FullName + @"\" + folder.Name))
                        Directory.CreateDirectory(cDirInfo.FullName + @"\" + folder.Name);

                    csDir = new DirectoryInfo(cDirInfo.FullName + @"\" + folder.Name);
                }
                await GenCodeForFolder(folder, tsDir, csDir, namespaceArg.Value + "." + folder.Name);
            }
            //exit
            System.Console.WriteLine("Done!");
        }

        private static async Task GenCodeForFolder(DirectoryInfo sourceDirInfo, DirectoryInfo tsDirInfo, DirectoryInfo csDirInfo,
            string namespaceArg)
        {
            //get only .json files
            var schemasFiles = sourceDirInfo.GetFiles().ToList().Where((f) => f.Extension.ToLower().Equals(".json")).ToList();
            System.Console.WriteLine("found {0} json schema files", schemasFiles.Count());
            foreach (var schemafile in schemasFiles)
            {
                await TryGenCode(schemafile, tsDirInfo, sourceDirInfo, csDirInfo, namespaceArg);
            }
            
        }

        private static async Task<bool> TryGenCode(FileInfo schemafile, DirectoryInfo tDirInfo, DirectoryInfo sDirInfo,
            DirectoryInfo cDirInfo, string namespaceArg)
        {
            System.Console.WriteLine("generating from {0} to", schemafile.Name);
            try
            {
                var schema = await JsonSchema.FromFileAsync(schemafile.FullName);
                //typescript
                if (tDirInfo != null)
                {
                    var generator = new TypeScriptGenerator(schema);
                    var typeScript = generator.GenerateFile();
                    Save(sDirInfo, tDirInfo, schemafile, typeScript, ".ts");
                }

                //c#
                if (cDirInfo != null)
                {
                    var generator = new CSharpGenerator(schema, new CSharpGeneratorSettings()
                    {
                        Namespace = namespaceArg ?? "Root",
                        PropertyNameGenerator = new AgodaPropertyNameGenerator(),
                        EnumNameGenerator = new AgodaEnumNameGenerator(),
                        TypeNameGenerator = new AgodaTypeNameGenerator(),
                    });
                    var cSharp = generator.GenerateFile();
                    Save(sDirInfo, cDirInfo, schemafile, cSharp, ".cs");
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine("Exception: {0} ", ex.Message);
                System.Console.WriteLine("Overstep and continue generating remaining?");
                if (!GetYesOrNoUserInput())
                    return false;
            }

            return true;
        }

        private static void Save(DirectoryInfo source, DirectoryInfo target, FileInfo schema, string data, string fileExtension)
        {
            var fileName = Path.GetFileNameWithoutExtension(schema.Name);
            fileName = fileName.Replace(".schema", "");
            var filePath = Path.Combine(target.FullName, fileName + fileExtension);
            var file = new FileInfo(filePath);
            if (!file.Exists) file.Create().Close();
            using (var sw = file.CreateText())
            {
                sw.Write(data);
            }
            System.Console.WriteLine(filePath);
        }

        static bool GetYesOrNoUserInput()
        {
            ConsoleKey response; // Creates a variable to hold the user's response.

            do
            {
                while (System.Console.KeyAvailable) // Flushes the input queue.
                    System.Console.ReadKey();

                System.Console.Write("Y or N? "); // Asks the user to answer with 'Y' or 'N'.
                response = System.Console.ReadKey().Key; // Gets the user's response.
                System.Console.WriteLine(); // Breaks the line.
            } while (response != ConsoleKey.Y && response != ConsoleKey.N); // If the user did not respond with a 'Y' or an 'N', repeat the loop.

            /* 
             * Return true if the user responded with 'Y', otherwise false.
             * 
             * We know the response was either 'Y' or 'N', so we can assume 
             * the response is 'N' if it is not 'Y'.
             */
            return response == ConsoleKey.Y;
        }
    }
}
