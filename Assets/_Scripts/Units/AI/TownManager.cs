#if false

using UnityEngine;
using Data;

public class TownManager()
{
    // List of available job objects
    // List of available home objects
    // Ref to spawners

    private TownData townData;
    private UnitRoleDataFactory roleDataFactory;

    public TownManager()
    {
        // On initiation, see if save file info exists
        // If null, search scene for building objects for jobs, homes, spawners
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

    // List of jobs available with status of availability

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

        newUnitData.SetJobType(DetermineJob());
        newUnitData.SetSocialClassType(DetermineSocialClass());
        newUnitData.SetGenderType(DetermineGender(townData.CurrentMales, townData.CurrentFemales));
        newUnitData.SetAgeType(DetermineAge());
        newUnitData.SetHome(DetermineHome());

        return newUnitData;
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


public interface IUnitRole()
{

}


public class CitizenRole: IUnitRole
{
    public JobType Job { get; protected set; }
    public SocialClassType SocialClass { get; protected set; }
    public GenderType Gender { get; protected set; }
    public AgeType Age { get; protected set; }
    public Transform Home { get; protected set; }
    public Transform Work { get; protected set; }

    public void SetJobType(JobType job) // Change to Job data
    {
        Job = job;
        SetWork(); // Sets location based on job data
    }

    public void SetSocialClassType(SocialClassType socialClass)
    {
        SocialClass = socialClass;
    }

    public void SetGenderType(GenderType gender)
    {
        Gender = gender;
    }

    public void SetAgeType(AgeType age)
    {
        Age = age;
    }

    public void SetHome(Transform home)
    {
        Home = home;
    }

    public void SetWork(Transform work)
    {
        Work = work;
    }
}

// MinionRole, EnemyRole, SoilderRole...

public interface IUnitDesign()
{
    void NewDesign(IUnitRole unitRole);

    static IUnitDesign CreateDefault()
    {
        return new MaleDesign();
    }
}

public class MaleDesign : IUnitDesign
{
    // Head
    // HatType
    // HatColor
    // HairType
    // HairColor
    // EyewareType
    // FaceDetails
    // Mustache
    public float NoseCurve { get; protected set; }
    public float NoseWidth { get; protected set; }
    public float NoseLength { get; protected set; }
    public float ChinWidth { get; protected set; }
    public float ChinLenght { get; protected set; }

    // Body
    // BodyStyle influenced by job
    // TieColor
    // JacketColor
    // PantColor
    // VestButtonColor
    // VestColor
    // ShirtColor
    // ShoeColor
    // ShoeType

    void NewDesign(IUnitRole unitRole);
}

public class FemaleDesign : IUnitDesign
{
    // Head
    // HatType
    // HatColor
    // HairType
    // HairColor
    // EyewareType
    // FaceDetails
    // Mustache
    public float NoseCurve { get; protected set; }
    public float NoseWidth { get; protected set; }
    public float NoseLength { get; protected set; }
    public float ChinWidth { get; protected set; }
    public float ChinLenght { get; protected set; }

    // Body
    // BodyStyle influenced by job
    // TieColor
    // JacketColor
    // PantColor
    // VestButtonColor
    // VestColor
    // ShirtColor
    // ShoeColor
    // ShoeType

    void NewDesign(IUnitRole unitRole);

}


public abstract class UnitDesignFactory : ScriptableObject {
    public abstract IUnitDesign CreateDesign();
}

[CreateAssetMenu(fileName = "MaleDesignFactory", menuName = "Units/Unit Design Factory/MaleDesignFactory")]
public class MaleDesignFactory: UnitDesignFactory {
    public override IUnitDesign CreateDesign()
    {
        return new MaleDesign();
    }
}

#endif