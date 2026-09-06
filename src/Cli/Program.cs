// ================================================================
//  Program.cs — the process entry point, and nothing else.
//
//  Everything that WAS here is in CliEntry.cs, unchanged. It moved because a local function of a
//  top-level program is private to `<Main>$` and callable by nobody, and `circuitrf serve` has to
//  call the verbs rather than re-implement them (R-aut-13). CliEntry.cs's header records the rest.
// ================================================================

return CircuitRF.Cli.CliEntry.Run(args);
