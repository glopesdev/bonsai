﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bonsai;
using Bonsai.Vision.Design;
using Bonsai.Vision;
using OpenCV.Net;
using Bonsai.Design;

[assembly: TypeVisualizer(typeof(KeyPointCollectionMashupVisualizer), Target = typeof(VisualizerMashup<ImageMashupVisualizer, KeyPointCollectionVisualizer>))]

namespace Bonsai.Vision.Design
{
    public class KeyPointCollectionMashupVisualizer : MashupTypeVisualizer
    {
        ImageMashupVisualizer visualizer;

        public override void Show(object value)
        {
            var keyPoints = (KeyPointCollection)value;
            KeyPointCollectionVisualizer.Draw(visualizer.VisualizerImage, keyPoints);
        }

        public override void Load(IServiceProvider provider)
        {
            visualizer = (ImageMashupVisualizer)provider.GetService(typeof(DialogMashupVisualizer));
        }

        public override void Unload()
        {
        }
    }
}
