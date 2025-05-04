using System.CommandLine;

namespace GMailThreadExtractor
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            var emailOption = new Option<string>(
                name: "--email",
                description: "The email address to use for authentication.") {
                    IsRequired = true
                };
            var passwordOption = new Option<string>(
                name: "--password",
                description: "The password to use for authentication.") {
                    IsRequired = true
                };
            var searchOption = new Option<string>(
                name: "--search",
                description: "The search query to filter emails.") {
                    IsRequired = false
                };
            var labelOption = new Option<string>(
                name: "--label",
                description: "The label to filter emails.") {
                    IsRequired = false
                };
            var outputOption = new Option<string>(
                name: "--output",
                description: "The output file path.") {
                    IsRequired = true
                };
            var rootCommand = new RootCommand("Extracts email threads from a Gmail account.")
            {
                emailOption,
                passwordOption,
                searchOption,
                labelOption,
                outputOption
            };

            rootCommand.SetHandler(async (email, password, search, label, output) =>
            {
                var extractor = new GMailThreadExtractor(email, password, "imap.gmail.com", 993);
                await extractor.ExtractThreadsAsync(output, search, label);
            }, 
            emailOption, passwordOption, searchOption, labelOption, outputOption);

            return await rootCommand.InvokeAsync(args);
        }
    }
}