using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Narazaka.Unity.CopyPrefabOverrides
{
    public class CopyPrefabOverrides : EditorWindow
    {
        [SerializeField] GameObject Source;
        [SerializeField] GameObject Target;
        SerializedObject SerializedObject;
        SerializedProperty SourceProperty;
        SerializedProperty TargetProperty;

        [MenuItem("Tools/Copy Prefab Overrides")]
        static void GetWindow()
        {
            GetWindow<CopyPrefabOverrides>();
        }

        void OnEnable()
        {
            SerializedObject = new SerializedObject(this);
            SourceProperty = SerializedObject.FindProperty(nameof(Source));
            TargetProperty = SerializedObject.FindProperty(nameof(Target));
        }

        void OnGUI()
        {
            SerializedObject.UpdateIfRequiredOrScript();
            EditorGUILayout.PropertyField(SourceProperty);
            EditorGUILayout.PropertyField(TargetProperty);
            SerializedObject.ApplyModifiedProperties();

            if (Target != null && !IsSceneObject(Target))
            {
                EditorGUILayout.HelpBox("Target must be a scene object.", MessageType.Error);
            }

            EditorGUI.BeginDisabledGroup(Source == null || Target == null || !IsSceneObject(Target));
            if (GUILayout.Button("Copy"))
            {
                Copy();
            }
            EditorGUI.EndDisabledGroup();
        }

        void Copy()
        {
            if (Source != null && Target != null)
            {
                var processor = new CopyProcessor(Source, Target, null, null);
                processor.Copy();
            }
        }

        bool IsSceneObject(GameObject obj)
        {
            return obj.scene.IsValid();
        }

        class CopyProcessor
        {
            public GameObject Source;
            public GameObject Target;
            public GameObject SourceOutermost;
            public GameObject TargetOutermost;

            Transform SourceSourceTransform;
            List<Warning> warnings = new List<Warning>();

            public CopyProcessor(GameObject source, GameObject target, GameObject sourceOutermost, GameObject targetOutermost)
            {
                Source = source;
                Target = target;
                SourceOutermost = sourceOutermost ?? Source;
                TargetOutermost = targetOutermost ?? Target;
                SourceSourceTransform = PrefabUtility.GetCorrespondingObjectFromSource(Source).transform;
            }

            public void Copy()
            {
                CopyAddedGameObjects();
                CopyRemovedGameObjects();
                CopyAddedComponents();
                CopyRemovedComponents();
                CopyObjectOverrides();
                if (warnings.Count > 0)
                {
                    foreach (var warning in warnings)
                    {
                        // Debug.LogWarning(warning.Message);
                    }
                }
            }

            void AddWarning(Warning warning)
            {
                warnings.Add(warning);
                Debug.LogWarning(warning.Message);
            }

            void CopyAddedGameObjects()
            {
                var addedGameObjects = PrefabUtility.GetAddedGameObjects(Source);
                foreach (var addedGameObject in addedGameObjects)
                {
                    var sourcePath = SourcePathOf(addedGameObject.instanceGameObject.transform);
                    var targetTransform = FindTargetTransform(sourcePath);
                    if (targetTransform == null)
                    {
                        var parentPath = SourcePathOf(addedGameObject.instanceGameObject.transform.parent);
                        var parentTransform = FindTargetTransform(parentPath);
                        if (parentTransform != null)
                        {
                            var prefab = PrefabUtility.GetCorrespondingObjectFromSource(addedGameObject.instanceGameObject);
                            GameObject newGameObject;
                            if (prefab != null)
                            {
                                newGameObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parentTransform);
                                CopySerializedAndReplaceReferences(addedGameObject.instanceGameObject, newGameObject);
                                new CopyProcessor(addedGameObject.instanceGameObject, newGameObject, Source, Target).Copy();
                                // CopyProperties(addedGameObject.instanceGameObject, newGameObject);
                            }
                            else
                            {
                                newGameObject = Object.Instantiate(addedGameObject.instanceGameObject, parentTransform);
                            }
                            newGameObject.name = addedGameObject.instanceGameObject.name;
                            ReplaceReferences(addedGameObject.instanceGameObject, newGameObject);
                        }
                        else
                        {
                            AddWarning(new ParentTransformNotFound(parentPath));
                        }
                    }
                }
            }

            void CopyRemovedGameObjects()
            {
                var removedGameObjects = PrefabUtility.GetRemovedGameObjects(Source);
                foreach (var removedGameObject in removedGameObjects)
                {
                    var path = SourceParentPathOf(removedGameObject.assetGameObject.transform);
                    var targetTransform = FindTargetTransform(path);
                    if (targetTransform != null)
                    {
                        Object.DestroyImmediate(targetTransform.gameObject);
                    }
                    else
                    {
                        AddWarning(new TargetTransformNotFound(path));
                    }
                }
            }

            void CopyAddedComponents()
            {
                var addedComponents = PrefabUtility.GetAddedComponents(Source);
                foreach (var component in addedComponents)
                {
                    var path = SourcePathOf(component.instanceComponent.transform);
                    var targetTransform = FindTargetTransform(path);
                    if (targetTransform != null)
                    {
                        var newComponent = targetTransform.gameObject.AddComponent(component.instanceComponent.GetType());
                        CopySerializedAndReplaceReferences(component.instanceComponent, newComponent);
                    }
                    else
                    {
                        AddWarning(new TargetTransformNotFound(path));
                    }
                }
            }

            void CopyRemovedComponents()
            {
                var removedComponents = PrefabUtility.GetRemovedComponents(Source);
                foreach (var component in removedComponents)
                {
                    var path = SourcePathOf(component.containingInstanceGameObject.transform);
                    var targetTransform = FindTargetTransform(path);
                    if (targetTransform != null)
                    {
                        var targetComponent = FindTargetComponent(targetTransform, component.assetComponent);
                        if (targetComponent != null)
                        {
                            Object.DestroyImmediate(targetComponent);
                        }
                        else
                        {
                            AddWarning(new TargetComponentNotFound(path));
                        }
                    }
                    else
                    {
                        AddWarning(new TargetTransformNotFound(path));
                    }
                }
            }

            void CopyObjectOverrides()
            {
                var objectOverrides = PrefabUtility.GetObjectOverrides(Source);
                foreach (var ov in objectOverrides)
                {
                    if (ov.instanceObject is GameObject gameObject)
                    {
                        var path = SourcePathOf(gameObject.transform);
                        var targetTransform = FindTargetTransform(path);
                        if (targetTransform != null)
                        {
                            CopySerializedAndReplaceReferences(ov.instanceObject, targetTransform.gameObject);
                        }
                        else
                        {
                            AddWarning(new TargetTransformNotFound(path));
                        }
                    }
                    else if (ov.instanceObject is Component component)
                    {
                        var path = SourcePathOf(component.transform);
                        var targetTransform = FindTargetTransform(path);
                        if (targetTransform != null)
                        {
                            var targetComponent = FindTargetComponent(targetTransform, component);
                            if (targetComponent != null)
                            {
                                // Debug.Log($"COMP {ov.instanceObject} => {targetComponent}");
                                CopySerializedAndReplaceReferences(ov.instanceObject, targetComponent);
                            }
                            else
                            {
                                AddWarning(new TargetComponentNotFound(path));
                            }
                        }
                        else
                        {
                            AddWarning(new TargetTransformNotFound(path));
                        }
                    }
                }
            }

            void CopyProperties(GameObject source, GameObject target)
            {
                CopySerializedAndReplaceReferences(source, target);
                // Copy all components
                var sourceComponents = source.GetComponents<Component>();
                foreach (var sourceComponent in sourceComponents)
                {
                    var targetComponent = FindTargetComponent(target.transform, sourceComponent);
                    if (targetComponent != null)
                    {
                        CopySerializedAndReplaceReferences(sourceComponent, targetComponent);
                    }
                    else
                    {
                        Debug.Log($"NEWCOMP {source} => {sourceComponent}");
                        var newComponent = target.AddComponent(sourceComponent.GetType());
                        CopySerializedAndReplaceReferences(sourceComponent, newComponent);
                    }
                }

                // Copy all child GameObjects
                for (int i = 0; i < source.transform.childCount; i++)
                {
                    var sourceChild = source.transform.GetChild(i).gameObject;
                    var targetChild = target.transform.Find(sourceChild.name)?.gameObject;
                    if (targetChild == null)
                    {
                        targetChild = new GameObject(sourceChild.name);
                        targetChild.transform.SetParent(target.transform);
                    }
                    CopyProperties(sourceChild, targetChild);
                }
            }

            void CopySerializedAndReplaceReferences(Object source, Object target)
            {
                EditorUtility.CopySerialized(source, target);
                ReplaceReferences(source, target);
            }

            void ReplaceReferences(Object source, Object target)
            {
                var serializedObject = new SerializedObject(target);
                var serializedSource = new SerializedObject(source);
                var property = serializedObject.GetIterator();
                while (property.NextVisible(true))
                {
                    if (property.propertyType == SerializedPropertyType.ObjectReference)
                    {
                        var sourceProperty = serializedSource.FindProperty(property.propertyPath);
                        if (sourceProperty != null && sourceProperty.objectReferenceValue != null && sourceProperty.propertyPath != "m_Script")
                        {
                            var sourceReference = sourceProperty.objectReferenceValue;
                            var targetReference = FindTargetReference(sourceReference);
                            if (targetReference != null && property.objectReferenceValue != targetReference)
                            {
                                property.objectReferenceValue = targetReference;
                            }
                        }
                    }
                }
                serializedObject.ApplyModifiedProperties();

                // Handle SkinnedMeshRenderer bones property
                if (source is SkinnedMeshRenderer sourceSkinnedMeshRenderer && target is SkinnedMeshRenderer targetSkinnedMeshRenderer)
                {
                    ReplaceSkinnedMeshRendererBones(sourceSkinnedMeshRenderer, targetSkinnedMeshRenderer);
                }
            }

            void ReplaceSkinnedMeshRendererBones(SkinnedMeshRenderer source, SkinnedMeshRenderer target)
            {
                var sourceBones = source.bones;
                var targetBones = new Transform[sourceBones.Length];
                bool bonesChanged = false;
                for (int i = 0; i < sourceBones.Length; i++)
                {
                    var sourceBone = sourceBones[i];
                    var sourcePath = SourcePathOf(sourceBone);
                    var targetBone = FindTargetTransform(sourcePath);
                    if (targetBone != null)
                    {
                        if (targetBones[i] != targetBone)
                        {
                            targetBones[i] = targetBone;
                            bonesChanged = true;
                        }
                    }
                    else
                    {
                        AddWarning(new TargetTransformNotFound(sourcePath));
                    }
                }
                if (bonesChanged)
                {
                    target.bones = targetBones;
                    EditorUtility.SetDirty(target);
                }
            }

            Object FindTargetReference(Object sourceReference)
            {
                if (sourceReference is GameObject sourceGameObject)
                {
                    //Debug.Log($"YES! GO {sourceReference}");
                    var sourcePath = SourcePathOf(sourceGameObject.transform);
                    return FindTargetTransform(sourcePath)?.gameObject;
                }
                else if (sourceReference is Component sourceComponent)
                {
                    //Debug.Log($"YES! co {sourceReference}");
                    var sourcePath = SourcePathOf(sourceComponent.transform);
                    var targetTransform = FindTargetTransform(sourcePath);
                    if (targetTransform == null)
                    {
                        Debug.Log($"NOT FOUND! {sourcePath}");
                    }
                    return FindTargetComponent(targetTransform, sourceComponent);
                }
                return null;
            }

            Transform FindTargetTransform(string path)
            {
                return TargetOutermost.transform.Find(path);
            }

            Component FindTargetComponent(Transform targetTransform, Component sourceComponent)
            {
                if (targetTransform == null) return null;

                var components = targetTransform.GetComponents(sourceComponent.GetType());
                Component bestMatch = null;
                int bestMatchScore = int.MaxValue;

                for (int i = 0; i < components.Length; i++)
                {
                    int score = CalculateComponentSimilarityScore(components[i], sourceComponent);
                    if (score < bestMatchScore)
                    {
                        bestMatch = components[i];
                        bestMatchScore = score;
                    }
                    else if (score == bestMatchScore)
                    {
                        // If scores are equal, consider the order of components
                        if (i == GetComponentIndex(sourceComponent))
                        {
                            bestMatch = components[i];
                        }
                    }
                }

                return bestMatch;
            }

            int CalculateComponentSimilarityScore(Component targetComponent, Component sourceComponent)
            {
                var targetSerialized = new SerializedObject(targetComponent);
                var sourceSerialized = new SerializedObject(sourceComponent);
                var targetProperty = targetSerialized.GetIterator();
                var sourceProperty = sourceSerialized.GetIterator();

                int score = 0;

                while (targetProperty.NextVisible(true) && sourceProperty.NextVisible(true))
                {
                    if (targetProperty.propertyType != sourceProperty.propertyType)
                    {
                        score += 10;
                        continue;
                    }

                    switch (targetProperty.propertyType)
                    {
                        case SerializedPropertyType.Integer:
                            score += Mathf.Abs(targetProperty.intValue - sourceProperty.intValue);
                            break;
                        case SerializedPropertyType.Boolean:
                            if (targetProperty.boolValue != sourceProperty.boolValue) score += 1;
                            break;
                        case SerializedPropertyType.Float:
                            score += Mathf.RoundToInt(Mathf.Abs(targetProperty.floatValue - sourceProperty.floatValue) * 100);
                            break;
                        case SerializedPropertyType.String:
                            if (targetProperty.stringValue != sourceProperty.stringValue) score += 5;
                            break;
                        case SerializedPropertyType.Color:
                            if (targetProperty.colorValue != sourceProperty.colorValue) score += 5;
                            break;
                        case SerializedPropertyType.ObjectReference:
                            if (targetProperty.objectReferenceValue != sourceProperty.objectReferenceValue) score += 5;
                            break;
                        case SerializedPropertyType.LayerMask:
                            if (targetProperty.intValue != sourceProperty.intValue) score += 5;
                            break;
                        case SerializedPropertyType.Enum:
                            if (targetProperty.enumValueIndex != sourceProperty.enumValueIndex) score += 5;
                            break;
                        case SerializedPropertyType.Vector2:
                            if (targetProperty.vector2Value != sourceProperty.vector2Value) score += 5;
                            break;
                        case SerializedPropertyType.Vector3:
                            if (targetProperty.vector3Value != sourceProperty.vector3Value) score += 5;
                            break;
                        case SerializedPropertyType.Vector4:
                            if (targetProperty.vector4Value != sourceProperty.vector4Value) score += 5;
                            break;
                        case SerializedPropertyType.Rect:
                            if (targetProperty.rectValue != sourceProperty.rectValue) score += 5;
                            break;
                        case SerializedPropertyType.ArraySize:
                            // Debug.Log($"Array size: {targetProperty.propertyPath} {sourceProperty.propertyPath}");
                            if (targetProperty.arraySize != sourceProperty.arraySize) score += 5;
                            break;
                        case SerializedPropertyType.Character:
                            if (targetProperty.intValue != sourceProperty.intValue) score += 5;
                            break;
                        case SerializedPropertyType.AnimationCurve:
                            if (targetProperty.animationCurveValue != sourceProperty.animationCurveValue) score += 5;
                            break;
                        case SerializedPropertyType.Bounds:
                            if (targetProperty.boundsValue != sourceProperty.boundsValue) score += 5;
                            break;
                        case SerializedPropertyType.Gradient:
                            if (!AreGradientsEqual(targetProperty.gradientValue, sourceProperty.gradientValue)) score += 5;
                            break;
                        default:
                            break;
                    }
                }

                return score;
            }

            bool AreGradientsEqual(Gradient gradient1, Gradient gradient2)
            {
                if (gradient1 == null || gradient2 == null) return gradient1 == gradient2;

                if (gradient1.colorKeys.Length != gradient2.colorKeys.Length ||
                    gradient1.alphaKeys.Length != gradient2.alphaKeys.Length)
                {
                    return false;
                }

                for (int i = 0; i < gradient1.colorKeys.Length; i++)
                {
                    if (gradient1.colorKeys[i].color != gradient2.colorKeys[i].color ||
                        gradient1.colorKeys[i].time != gradient2.colorKeys[i].time)
                    {
                        return false;
                    }
                }

                for (int i = 0; i < gradient1.alphaKeys.Length; i++)
                {
                    if (gradient1.alphaKeys[i].alpha != gradient2.alphaKeys[i].alpha ||
                        gradient1.alphaKeys[i].time != gradient2.alphaKeys[i].time)
                    {
                        return false;
                    }
                }

                return true;
            }

            int GetComponentIndex(Component component)
            {
                var components = component.GetComponents(component.GetType());
                for (int i = 0; i < components.Length; i++)
                {
                    if (components[i] == component)
                    {
                        return i;
                    }
                }
                return -1;
            }

            string SourcePathOf(Transform target)
            {
                return PathOf(target, SourceOutermost.transform);
            }

            string SourceParentPathOf(Transform target)
            {
                return PathOf(target, SourceSourceTransform);
            }

            string PathOf(Transform target, Transform rootTransform)
            {
                var paths = new List<string>();
                var current = target;
                while (current != rootTransform)
                {
                    // Debug.Log($"root={rootTransform}({rootTransform.GetInstanceID()}) current={current}({current.GetInstanceID()})");
                    paths.Add(current.name);
                    current = current.parent;
                }
                paths.Reverse();
                return string.Join("/", paths);
            }
        }

        abstract class Warning
        {
            public abstract string Message { get; }
        }

        class TargetTransformNotFound : Warning
        {
            public string Path { get; }
            public override string Message => $"Target transform not found for path: {Path}";

            public TargetTransformNotFound(string path)
            {
                Path = path;
            }
        }

        class ParentTransformNotFound : Warning
        {
            public string Path { get; }
            public override string Message => $"Parent transform not found for path: {Path}";

            public ParentTransformNotFound(string path)
            {
                Path = path;
            }
        }

        class TargetComponentNotFound : Warning
        {
            public string Path { get; }
            public override string Message => $"Target component not found for path: {Path}";

            public TargetComponentNotFound(string path)
            {
                Path = path;
            }
        }
    }
}