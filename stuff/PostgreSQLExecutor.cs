using NppDB.Comm;
using System;
using System.Collections.Generic;
using System.Data.Odbc;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Antlr4.Runtime;
using System.IO;
using System.Data;
using Npgsql;

namespace NppDB.PostgreSQL
{
    public class PostgreSQLLexerErrorListener : ConsoleErrorListener<int>
    {
        public new static readonly PostgreSQLLexerErrorListener Instance = new PostgreSQLLexerErrorListener();

        public override void SyntaxError(TextWriter output, IRecognizer recognizer,
            int offendingSymbol, int line, int col, string msg, RecognitionException e)
        {
            Console.WriteLine($"LEXER ERROR: {e?.GetType().ToString() ?? ""}: {msg} ({line}:{col})");
        }
    }

    public class PostgreSQLParserErrorListener : BaseErrorListener
    {
        private readonly IList<ParserError> _errors = new List<ParserError>();
        public IList<ParserError> Errors => _errors;

        public override void SyntaxError(TextWriter output, IRecognizer recognizer,
            IToken offendingSymbol, int line, int col, string msg, RecognitionException e)
        {
            Console.WriteLine($"PARSER ERROR: {e?.GetType().ToString() ?? ""}: {msg} ({line}:{col})");
            _errors.Add(new ParserError
            {
                Text = msg,
                StartLine = line,
                StartColumn = col,
                StartOffset = offendingSymbol.StartIndex,
                // StopLine = ,
                // StopColumn = ,
                StopOffset = offendingSymbol.StopIndex,
            });
        }
    }

    public class PostgreSQLExecutor : ISQLExecutor
    {
        private Thread _execTh;
        private readonly Func<NpgsqlConnection> _connector;

        public PostgreSQLExecutor(Func<NpgsqlConnection> connector)
        {
            _connector = connector;
        }

        public void Execute(IList<string> sqlQueries, Action<IList<CommandResult>> callback)
        {
            Console.WriteLine("execute");
            _execTh = new Thread(new ThreadStart(
                delegate
                {
                    var results = new List<CommandResult>();
                    string lastSql = null;
                    try
                    {
                        using (var conn = _connector())
                        {
                            conn.Open();
                            foreach (var sql in sqlQueries)
                            {
                                Console.WriteLine(sql);
                                if (string.IsNullOrWhiteSpace(sql)) continue;
                                lastSql = sql;

                                Console.WriteLine($"SQL: <{sql}>");
                                var cmd = new NpgsqlCommand(sql, conn);
                                var rd = cmd.ExecuteReader();
                                var dt = new DataTable();
                                dt.Load(rd);
                                Console.WriteLine(dt.Columns.Count);
                                results.Add(new CommandResult { CommandText = sql, QueryResult = dt, RecordsAffected = rd.RecordsAffected });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        results.Add(new CommandResult { CommandText = lastSql, Error = ex });
                        callback(results);
                        return;
                    }
                    callback(results);
                    _execTh = null;
                }));
            _execTh.IsBackground = true;
            _execTh.Start();
        }

        public ParserResult Parse(string sqlText, CaretPosition caretPosition)
        {

            Console.WriteLine(sqlText);
            var input = CharStreams.fromString(sqlText);

            var lexer = new PostgreSQLLexer(input);
            lexer.RemoveErrorListeners();
            lexer.AddErrorListener(PostgreSQLLexerErrorListener.Instance);

            CommonTokenStream tokens;
            try
            {
                tokens = new CommonTokenStream(lexer);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Lexer Exception: {e}");
                throw e;
            }

            var parserErrorListener = new PostgreSQLParserErrorListener();
            var parser = new PostgreSQLParser(tokens);
            parser.RemoveErrorListeners();
            parser.AddErrorListener(parserErrorListener);
            try
            {
                var tree = parser.root();
                var enclosingCommandIndex = tree.CollectCommands(caretPosition, " ", PostgreSQLParser.SEMI, out var commands);
                return new ParserResult
                {
                    Errors = parserErrorListener.Errors,
                    Commands = commands.ToList<ParsedCommand>(),
                    EnclosingCommandIndex = enclosingCommandIndex
                };
            }
            catch (Exception e)
            {
                Console.WriteLine($"Parser Exception: {e}");
                throw e;
            }
        }

        public bool CanExecute()
        {
            return !CanStop();
        }

        public bool CanStop()
        {
            return _execTh != null && (_execTh.ThreadState & ThreadState.Running) != 0;
        }

        public void Stop()
        {
            if (!CanStop()) return;
            _execTh?.Abort();
            _execTh = null;
        }
    }
}
