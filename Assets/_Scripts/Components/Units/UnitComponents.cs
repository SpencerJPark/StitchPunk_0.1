using Unity.Entities;


public struct Health : IComponentData {
    
    public int healthAmount;
    public int healthAmountMax;
    public bool onHealthChanged;
    public bool onDead;
}

public struct HealthBar : IComponentData {

    public Entity barVisualEntity;
    public Entity healthEntity;

}

public struct Selected : IComponentData, IEnableableComponent {

    public Entity visualEntity;
    public float showScale;

    public bool onSelected;
    public bool onDeselected;
}