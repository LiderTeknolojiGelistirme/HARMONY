using UnityEngine;
using Zenject;
using RosSharp.RosBridgeClient;

public class GameInstaller : MonoInstaller
{
    [SerializeField] private GameConfig gameConfig;
    
    public override void InstallBindings()
    {
        // 1. Config'i bind et
        if (gameConfig != null)
        {
            Container.BindInstance(gameConfig).AsSingle();
        }
        else
        {
            Debug.LogError("GameConfig atanmamış! GameInstaller'da GameConfig referansını atayın.");
        }
        
        // 2. DatabaseManager'ı bind et
        // Eğer DatabaseManager bir MonoBehaviour ise ve sahnede varsa .FromComponentInHierarchy() eklemen daha güvenli olur.
        // Ama şu anki haliyle çalışıyorsa dokunmuyoruz.
        Container.Bind<DatabaseManager>().AsSingle();
        
        // 3. RosConnector'ı bind et
        // Önce gameConfig'den dene, yoksa sahneden bul
        if (gameConfig != null && gameConfig.rosConnector != null)
        {
            Container.Bind<RosConnector>()
                .FromInstance(gameConfig.rosConnector)
                .AsSingle();
        }
        else
        {
            // Sahnede var olan RosConnector'ı bul ve bind et
            Container.Bind<RosConnector>()
                .FromComponentInHierarchy()
                .AsSingle();
        }
        
        // -----------------------------------------------------------------------
        // SAHNEDEKİ MEVCUT COMPONENTLERİ BIND ETME KISMI
        // Factory'ler kaldırıldı, sadece sahnede var olanları bulup eşleştirecek.
        // -----------------------------------------------------------------------

        // ExecCommandViaSocket
        Container.Bind<ExecCommandViaSocket>()
            .FromComponentInHierarchy()
            .AsSingle();
        
        // ImageSubscriber
        Container.Bind<ImageSubscriber>()
            .FromComponentInHierarchy()
            .AsSingle();
        
        // JointStateSubscriber
        Container.Bind<JointStateSubscriber>()
            .FromComponentInHierarchy()
            .AsSingle();
        
        // MagicianSimulator
        Container.Bind<MagicianSimulator>()
            .FromComponentInHierarchy()
            .AsSingle();
        
        // RobotSpecsSubscriber
        Container.Bind<RobotSpecsSubscriber>()
            .FromComponentInHierarchy()
            .AsSingle();
        
        // RosLogSubscriber
        Container.Bind<RosLogSubscriber>()
            .FromComponentInHierarchy()
            .AsSingle();
        
        // TrajectoryEventSubscriber
        Container.Bind<TrajectoryEventSubscriber>()
            .FromComponentInHierarchy()
            .AsSingle();
        
        // XBOT UI Subscribers
        Container.Bind<JointDetailSubscriber>()
            .FromComponentInHierarchy()
            .AsSingle();
        
        Container.Bind<RobotStatusSubscriber>()
            .FromComponentInHierarchy()
            .AsSingle();
        
        Container.Bind<JointSliderSubscriber>()
            .FromComponentInHierarchy()
            .AsSingle();
        
        Container.Bind<ControllerStateSubscriber>()
            .FromComponentInHierarchy()
            .AsSingle();
        
        // RobotJointController
        Container.Bind<RobotJointController>()
            .FromComponentInHierarchy()
            .AsSingle();
    }
}