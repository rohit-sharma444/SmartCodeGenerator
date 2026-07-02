Overview

CodeGenerator is an advanced without AI-powered .NET code generation and management engine. It allows developers to create, modify, analyze, and maintain C# codebases using natural language commands.
The system combines:

Roslyn (Microsoft.CodeAnalysis) for deep code understanding and generation
Natural Language Processing (custom intent detection, entity extraction, vocabulary/synonym handling)
Project-aware operations (analyzes existing .csproj solutions, handles dependencies, circular references, etc.)

It consists of two main components:

CodeGenerationEngine - Core library with all the intelligence
CodeGenerator - Console application/entry point

Target Framework: .NET 10.0

Key Features
1. Natural Language Interface

Parse commands like: "Create a User class with name, email (required, max 100 chars), and age (range 18-120)"
Intent detection with confidence scoring
Entity extraction (classes, properties, methods, validation rules)
Context awareness and session memory

2. Code Generation Capabilities

Class Generation with properties, validation attributes ([Required], [StringLength], [Range], etc.)
Method Generation with parameters and basic implementation
Specialized generators:
Controllers (API)
Services
Repositories
Interfaces
DTOs / Entities


3. Project Management & Refactoring

Analyze Project/Class – Deep inspection using Roslyn
Rename / Delete / Restore classes (with safe handling)
List classes, methods, properties
Circular dependency detection
Project compilation & validation
Design document generation

4. Robust Validation & Safety

Duplicate class/property detection
Namespace validation
Type inference
Generic type support
Semantic validation

5. Developer Experience

Interactive console
Command suggestions
Help system
File system helpers
Encryption utilities (for sensitive contexts)


Project Structure
textCodeGenerator/
├── CodeGenerationEngine/          # Core Class Library
│   ├── Core/                      # Main business logic
│   ├── Models/                    # Data models (ClassDefinition, ParsedCommand, etc.)
│   ├── Helper/                    # Utilities (FileHelper, InteractiveConsole, etc.)
│   ├── Service/                   # High-level services
│   ├── Factory/
│   ├── Rules/
│   ├── Tokenizer/
│   └── CodeGenerationEngine.csproj
│
├── CodeGenerator/                 # Console App
│   ├── Program.cs
│   └── CodeGenerator.csproj
│
└── Various build artifacts, bins, objs
Important Directories & Files

Core/CodeGenerator.cs: Main code emission logic
Core/NaturalLanguageCodeEngine.cs: Orchestrates command processing
Core/CommandParser.cs: Tokenization and parsing
Core/IntentAnalyzer.cs + SemanticIntentAnalyzer.cs: Understanding user intent
Models/: Strongly-typed representations of code elements and commands
Core/RoslynClassGenerator.cs: Heavy Roslyn integration


Architecture Highlights
Command Flow

Input → CommandNormalizer + CommandParser
Intent Detection → IntentAnalyzer / SemanticIntentAnalyzer
Entity Extraction → EntityExtractor, ClassExtractor, etc.
Validation → ValidationEngine, multiple specialized validators
Execution → CodeGenerationEngine.Generate() or specific services
Output → File writing + feedback

Extensibility

CommandDictionary and CommandCatalog for easy addition of new commands
Rule-based validation system (IPropertyRule, ValidationRuleParser)
Vocabulary and synonym support for natural language flexibility


Technologies & Dependencies

Roslyn: Microsoft.CodeAnalysis.CSharp (v5.3.0) – for parsing, analysis, and generation
.NET 10.0 with nullable reference types and implicit usings
Custom NLP components (no external LLM dependency in core)
Humanizer (via transitive deps)


How to Run

Build the solution
Run CodeGenerator executable
Use natural language commands in the interactive console

Example commands the system supports:

Create class User with properties: Id int, Name string required, Email string
Analyze project
List classes
Rename class OldName to NewName
Generate API controller for User


Potential Use Cases

Rapid prototyping of domain models
Consistent code scaffolding across large solutions
Automated refactoring assistance
Educational tool for learning C# patterns
Integration into IDEs or CI/CD pipelines


Status & Notes

The project appears to be in active development (recent build artifacts from June 2026)
Strong focus on safety (validation, backups for deletions, duplicate detection)
Highly modular and extensible architecture

This system represents a sophisticated "AI Pair Programmer" specialized for .NET/C# project maintenance and generation.
Created: June 2026
Framework: .NET 10.0
License: Not specified (proprietary/custom)