// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Cli.Arguments;
using Bicep.Cli.Logging;
using Bicep.Cli.Services;
using Bicep.Core.Diagnostics;
using Bicep.Core.Emit;
using Bicep.Core.FileSystem;
using Bicep.Core.Modules;
using Bicep.Core.Parsing;
using Bicep.Core.Registry;
using Bicep.Core.Utils;
using System;
using System.IO;

namespace Bicep.Cli.Commands
{
    public class PublishCommand : ICommand
    {
        private readonly IDiagnosticLogger diagnosticLogger;
        private readonly CompilationService compilationService;
        private readonly CompilationWriter compilationWriter;
        private readonly IModuleDispatcher moduleDispatcher;

        public PublishCommand(
            IDiagnosticLogger diagnosticLogger,
            CompilationService compilationService,
            CompilationWriter compilationWriter,
            IModuleDispatcher moduleDispatcher)
        {
            this.diagnosticLogger = diagnosticLogger;
            this.compilationService = compilationService;
            this.compilationWriter = compilationWriter;
            this.moduleDispatcher = moduleDispatcher;
        }

        public int Run(PublishArguments args)
        {
            var moduleReference = ValidateReference(args.TargetModuleReference);

            var inputPath = PathHelper.ResolvePath(args.InputFile);

            var compilation = compilationService.Compile(inputPath);

            if(diagnosticLogger.ErrorCount > 0)
            {
                // can't publish if we can't compile
                return 1;
            }

            var stream = new MemoryStream();
            compilationWriter.ToStream(compilation, stream);

            stream.Position = 0;
            // TODO: make it async
            this.moduleDispatcher.PublishModule(moduleReference, stream).Wait();

            return 0;
        }

        private ModuleReference ValidateReference(string targetModuleReference)
        {
            var moduleReference = this.moduleDispatcher.TryGetModuleReference(targetModuleReference, out var failureBuilder);
            if(moduleReference is null)
            {
                var message = failureBuilder!(DiagnosticBuilder.ForPosition(new TextSpan(0, 0))).Message;
                throw new BicepException(message);
            }

            if(!this.moduleDispatcher.GetRegistryCapabilities(moduleReference).HasFlag(RegistryCapabilities.Publish))
            {
                throw new BicepException($"The specified target module reference \"{targetModuleReference}\" cannot be published.");
            }

            return moduleReference;
        }
    }
}
