using UnityEngine;
using Data;

public class TownManager()
{
    // List of available job objects
    // List of available home objects
    // Ref to spawners

    public void PopulateTown()
    {
        // called when a town is first booted up
        // Fires up all spawners (located in homes) based on criteria
        
    }

    public void RebalanceTownPopulation()
    {
        // Called Once a night
    }

}

public class TownOverviewData()
{
    public int MaxPopulation { get; protected set; }
    public int MaxRecoveryRatePerDay { get; protected set; }
    
    public int MaxMales { get; protected set; }
    public int MaxFemales { get; protected set; }
    
    public int MaxElderly { get; protected set; } // 20%
    public int MaxAdults { get; protected set; } // 70%
    public int MaxChildren { get; protected set; } // 10%

// Manipulable variables
    public int CurrentPopulation;

    public int CurrentMales; // 50%
    public int CurrentFemales; // 50%

    public int CurrentElderly; // 20%
    public int CurrentAdults; // 70%
    public int CurrentChildren; // 10%

    TownOverviewData(int maxPopulation)
    {
        MaxPopulation = maxPopulation;
        MaxRecoveryRatePerDay = maxPopulation * 0.05; // 5%
        MaxMales = maxPopulation * 0.5;
        MaxFemales = maxPopulation * 0.5;
        MaxElderly = maxPopulation * 0.2;
        MaxAdults = maxPopulation * 0.7;
        MaxChildren = maxPopulation * 0.1;
    }
}


public class TownRoleData()
{
    public JobType Job { get; protected set; }
    public SocialClassType SocialClass { get; protected set; }
    public GenderType Gender { get; protected set; }
    public AgeType Age { get; protected set; }
    public Transform Home { get; protected set; }
    public Transform Work { get; protected set; }

    TownRoleData(TownOverviewData townData) // Takes a data snapshot of the town and then uses that information to set data
    {
        DetermineJob();
        DetermineSocialClass();
        DetermineGender(townData.CurrentMales, townData.CurrentFemales);
        DetermineAge();
        DetermineHome();

    }
    private void DetermineJob()
    {
        // logic for picking job

        // Set Job
        // Set Work
    }

    private void DetermineGender(int currentMales, int currentFemales)
    {
        // influenced by job first then town needs
    }

    private void DetermineAge()
    {
        // influenced by job first then town needs
    }
    private void DetermineSocialClass()
    {
        // influenced by job first then homeneeds
    }

    private void DetermineHome()
    {
        // influenced by social class and gender then availability
    }

}