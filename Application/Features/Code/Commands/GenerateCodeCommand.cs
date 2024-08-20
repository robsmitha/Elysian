using Elysian.Application.Features.Code.Models;
using Elysian.Domain.Customization;
using MediatR;
using NJsonSchema;
using NJsonSchema.CodeGeneration.CSharp;
using NJsonSchema.CodeGeneration.TypeScript;
using System.Text;
using System.Text.RegularExpressions;

namespace Elysian.Application.Features.Code.Commands
{
    public class GenerateCodeCommand(CodeGenerationRequest generateCodeRequest) : IRequest<CodeGenerationResponse>
    {
        public CodeGenerationRequest CodeGenerationOptions { get; set; } = generateCodeRequest;
        public class Handler() : IRequestHandler<GenerateCodeCommand, CodeGenerationResponse>
        {
            public Task<CodeGenerationResponse> Handle(GenerateCodeCommand request, CancellationToken cancellationToken)
            {
                var jsonSchema = JsonSchema.FromSampleJson(request.CodeGenerationOptions.SampleJson);

                string code;
                if (request.CodeGenerationOptions.Language.Equals("typescript", StringComparison.InvariantCultureIgnoreCase))
                {
                    var generator = new TypeScriptGenerator(jsonSchema);

                    var typescriptCode = new StringBuilder();
                    typescriptCode.AppendLine("/* eslint-disable prefer-const */");
                    typescriptCode.AppendLine("/* eslint-disable no-var */");
                    typescriptCode.AppendLine("/* eslint-disable no-prototype-builtins */");
                    typescriptCode.AppendLine();
                    typescriptCode.AppendLine(generator.GenerateFile(request.CodeGenerationOptions.ResponseName));
                    code = typescriptCode.ToString();
                }
                else
                {
                    var generator = new CSharpGenerator(jsonSchema, new CSharpGeneratorSettings
                    {
                        Namespace = $"{request.CodeGenerationOptions.Namespace}.{request.CodeGenerationOptions.ResponseName}",
                        RequiredPropertiesMustBeDefined = true,
                        GenerateOptionalPropertiesAsNullable = true,
                        PropertyNameGenerator = new PascalCasePropertyNameGenerator()
                    });
                    var wholeFile = generator.GenerateFile(request.CodeGenerationOptions.ResponseName);

                    var additionalPropertiesPattern = @"\n\s*\n\s*\n\s*\n\s*private System\.Collections\.Generic\.IDictionary<string, object> _additionalProperties;\s*\[Newtonsoft\.Json\.JsonExtensionData\]\s*public System\.Collections\.Generic\.IDictionary<string, object> AdditionalProperties\s*\{\s*get \{ return _additionalProperties \?\? \(_additionalProperties = new System\.Collections\.Generic\.Dictionary<string, object>\(\)\); \}\s*set \{ _additionalProperties = value; \}\s*\}";

                    wholeFile = Regex.Replace(wholeFile, additionalPropertiesPattern, "", RegexOptions.Multiline);

                    code = wholeFile;
                }

                return Task.FromResult(new CodeGenerationResponse(code));
            }
        }
    }
}
