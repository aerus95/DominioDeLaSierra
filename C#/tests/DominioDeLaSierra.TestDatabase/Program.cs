if (args.Length != 1)
{
    Console.Error.WriteLine("Uso: prepare | cleanup | drop");
    return 1;
}

try
{
    switch (args[0])
    {
        case "prepare":
            await DominioDeLaSierra.TestDatabase.TestCatalog.PrepareAsync();
            Console.WriteLine("Base dominio_sierra_tests preparada.");
            return 0;
        case "cleanup":
            await DominioDeLaSierra.TestDatabase.TestCatalog.CleanupAsync();
            Console.WriteLine("Datos de dominio_sierra_tests eliminados. El almacén principal se conserva.");
            return 0;
        case "drop":
            await DominioDeLaSierra.TestDatabase.TestCatalog.DropAsync();
            Console.WriteLine("Base dominio_sierra_tests eliminada.");
            return 0;
        default:
            Console.Error.WriteLine("Uso: prepare | cleanup | drop");
            return 1;
    }
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
