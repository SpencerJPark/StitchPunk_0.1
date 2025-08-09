#if false

using UnityEngine;
using Data;

public class TownManager()
{
    // List of available Role objects
    // List of available home objects
    // Ref to spawners

    private TownData townData;
    private UnitRoleDataFactory roleDataFactory;

    public TownManager()
    {
        // On initiation, see if save file info exists
        // If null, search scene for building objects for Roles, homes, spawners
        // Create Town Data
        // Call PopulateTown which 

        townData = TownData();
        roleDataFactory = UnitRoleDataFactory(townData)
    }

    public void PopulateTown()
    {
        // called when a town is first booted up
        // Creates an Unit factory with the town data and then a method is called that returns a list of Town Role Datas
        // That will be used to create Units
        // Fires up all spawners (located in homes) based on criteria

        roleDataFactory.CreateNewUnitRoleDataList();

        // pass list to spawnermanager

    }

    public void RebalanceTownPopulation()
    {
        // Called Once a night
    }

}

public class TownData()
{
    public int MaxPopulation { get; protected set; }
    public int MaxRecoveryRatePerDay { get; protected set; }

    // List of Roles available with status of availability

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

    TownData(int maxPopulation)
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


public class UnitRoleFactory()
{
    private void TownData;

    UnitRoleDataFactory(TownData townData) // Takes a data snapshot of the town and then uses that information to set data
    {
        TownData = townData;
    }

    public list<UnitRoleData> CreateNewUnitRoleDataList()
    {
        // logic to call method below repeatedly to make a list and then return
        CreateUnit()
    }

    public UnitRoleData CreateUnit()
    {
        UnitRoleData newUnitData = UnitRoleData();

        newUnitData.SetRoleType(DetermineRole());
        newUnitData.SetSocialClassType(DetermineSocialClass());
        newUnitData.SetGenderType(DetermineGender(townData.CurrentMales, townData.CurrentFemales));
        newUnitData.SetAgeType(DetermineAge());
        newUnitData.SetHome(DetermineHome());

        return newUnitData;
    }
    private void DetermineRole()
    {
        // logic for picking Role

        // Set Role
        // Set Work
    }

    private void DetermineGender(int currentMales, int currentFemales)
    {
        // influenced by Role first then town needs
    }

    private void DetermineAge()
    {
        // influenced by Role first then town needs
    }
    private void DetermineSocialClass()
    {
        // influenced by Role first then homeneeds
    }

    private void DetermineHome()
    {
        // influenced by social class and gender then availability
    }
}





#endif