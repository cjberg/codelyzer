﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Codelyzer.Analysis.Model
{
    public class PropertyBlock : UstNode
    {
        [JsonProperty("base-type", Order = 10)]
        public string BaseType { get; set; }

        [JsonProperty("base-type-original-def", Order = 11)]
        public string BaseTypeOriginalDefinition { get; set; }

        [JsonProperty("base-list", Order = 12)]
        public List<string> BaseList { get; set; }

        [JsonProperty("modifiers", Order = 20)]
        public string Modifiers { get; set; }

        [JsonProperty("references", Order = 99)]
        public Reference Reference { get; set; }
        public string SemanticAssembly { get; set; }

        [JsonProperty("parameters", Order = 100)]
        public List<Parameter> Parameters { get; set; }

        public PropertyBlock()
            : base(IdConstants.PropertyBlockName)
        {
            Reference = new Reference();
        }

        public override bool Equals(object obj)
        {
            if (obj is PropertyBlock)
            {
                return Equals(obj as PropertyBlock);
            }
            return false;
        }

        public bool Equals(PropertyBlock compareNode)
        {
            return
                compareNode != null &&
                BaseType?.Equals(compareNode.BaseType) != false &&
                BaseTypeOriginalDefinition?.Equals(compareNode.BaseTypeOriginalDefinition) != false &&
                Modifiers?.Equals(compareNode.Modifiers) != false &&
                SemanticAssembly?.Equals(compareNode.SemanticAssembly) != false &&
                base.Equals(compareNode);

        }
        public override int GetHashCode()
        {
            return HashCode.Combine(BaseType, BaseTypeOriginalDefinition, Modifiers, SemanticAssembly, base.GetHashCode());
        }
    }
}
