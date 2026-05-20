public interface IEnemyValidatedComponent
{
    bool IsConfigured { get; }

    bool ValidateStaticDependencies();

    bool ValidateRuntimeDependencies();
}