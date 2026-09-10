namespace HouseOfSilence.Doors
{
    /// <summary>
    /// Porte standard de la maison.
    ///
    /// Couvre a la fois le "NormalDoor" et le "LockedDoor" du cahier des charges :
    /// une porte simplement verrouillee n'est pas un autre comportement, c'est
    /// cette meme porte avec Start Locked coche. Elle sera alors deverrouillee
    /// par un script, un objectif ou un evenement via Unlock().
    ///
    /// Quand le deverrouillage doit venir d'une cle, utiliser KeyDoor.
    /// Quand il doit venir d'un objectif, utiliser ObjectiveDoor.
    /// </summary>
    public class Door : DoorBase
    {
    }
}
