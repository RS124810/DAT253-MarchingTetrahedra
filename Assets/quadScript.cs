using UnityEngine;
using System.Linq;
using System;
using System.Collections.Generic;
using System.IO;

public class quadScript : MonoBehaviour
{

    // Dicom har et "levende" dictionary som leses fra xml ved initDicom
    // slices må sorteres, og det basert på en tag, men at pixeldata lesing er en separat operasjon, derfor har vi nullpeker til pixeldata
    // dicomfile lagres slik at fil ikke må leses enda en gang når pixeldata hentes

    // member variables of quadScript, accessible from any function
    Slice[] _slices;
    int _numSlices;
    int _minIntensity;
    int _maxIntensity;
    float _iso=0.5f;

    meshScript mscript;
    List<Vector3> _vertices = new List<Vector3>();
    List<int> _indices = new List<int>();

    List<Vector3> _v = new List<Vector3>();
    List<int> _i = new List<int>();

    private Point[,,] _kubenett;

    private float _steps;       //bomet litt på variabel navn, dette er antall steps
    private int _size = 512;     //number of points for all x,y,z -> size of kubenett
    private float _opplosning;  //lengden fra et punkt til neste punkt
    private float _opplosningZ;

    private int _kolonner;
    private int _rekker;
    private int _dybde;

    private int _indexNr = 0;

    private string _filename = "MeshFil.obj";

    void Start()
    {

        Slice.initDicom();

        string dicomfilepath = Application.dataPath + @"\..\dicomdata\"; // Application.dataPath is in the assets folder, but these files are "managed", so we go one level up


        _slices = processSlices(dicomfilepath);     // loads slices from the folder above
        setTexture(_slices[0]);                     // shows the first slice

        //  gets the mesh object and uses it to create a diagonal line
        mscript = GameObject.Find("GameObjectMesh").GetComponent<meshScript>();
        
        mscript.createMeshGeometry(_vertices, _indices);

        //init key values
        _kolonner = _size;
        _rekker = _size;
        _dybde = 354;//_size;

        _steps = _size - 1.0f;
        _opplosning = 1.0f / _steps;
        
        _opplosningZ = 1.0f / (_dybde-1.0f);    
        
    }

    Slice[] processSlices(string dicomfilepath)
    {
        string[] dicomfilenames = Directory.GetFiles(dicomfilepath, "*.IMA");


        _numSlices = dicomfilenames.Length;

        Slice[] slices = new Slice[_numSlices];

        float max = -1;
        float min = 99999;
        for (int i = 0; i < _numSlices; i++)
        {
            string filename = dicomfilenames[i];
            slices[i] = new Slice(filename);
            SliceInfo info = slices[i].sliceInfo;
            if (info.LargestImagePixelValue > max) max = info.LargestImagePixelValue;
            if (info.SmallestImagePixelValue < min) min = info.SmallestImagePixelValue;
            // Del dataen på max før den settes inn i tekstur
            // alternativet er å dele på 2^dicombitdepth,  men det ville blitt 4096 i dette tilfelle

        }
        print("Number of slices read:" + _numSlices);
        print("Max intensity in all slices:" + max);
        print("Min intensity in all slices:" + min);

        _minIntensity = (int)min;
        _maxIntensity = (int)max;
        //_iso = 0;

        Array.Sort(slices);

        return slices;
    }

    void setTexture(Slice slice)
    {
        int xdim = slice.sliceInfo.Rows;
        int ydim = slice.sliceInfo.Columns;        

        var texture = new Texture2D(xdim, ydim, TextureFormat.RGB24, false);     // garbage collector will tackle that it is new'ed 

        ushort[] pixels = slice.getPixels();
        
        for (int y = 0; y < ydim; y++)
            for (int x = 0; x < xdim; x++)
            {
                float val = pixelval(new Vector2(x, y), xdim, pixels);
                float v = (val - _minIntensity) / _maxIntensity;      // maps [_minIntensity,_maxIntensity] to [0,1] , i.e.  _minIntensity to black and _maxIntensity to white
                
                texture.SetPixel(x, y, new UnityEngine.Color(v, v, v));
            }
        

        texture.filterMode = FilterMode.Point;  // nearest neigbor interpolation is used.  (alternative is FilterMode.Bilinear)
        texture.Apply();  // Apply all SetPixel calls
        GetComponent<Renderer>().material.mainTexture = texture;
    }

    ushort pixelval(Vector2 p, int xdim, ushort[] pixels)
    {
        return pixels[(int)p.x + (int)p.y * xdim];
    }


    Vector2 vec2(float x, float y)
    {
        return new Vector2(x, y);
    }


    // Update is called once per frame
    void Update()
    {


    }

    public void slicePosSliderChange(float val)
    {
        setTexture(_slices[(int)val]);
        print("slicePosSliderChange:" + val);
        
    }

    public void sliceIsoSliderChange(float val)
    {
        //_iso = val * 0.5f;
        print("Iso Slider change " + _iso);
        //Setup3D();
    }

    public void button1Pushed()
    {
        print("button1Pushed");
        WrightToFile();
    }

    public void button2Pushed()
    {
        print("button2Pushed Marching tetrahedras");
        //Clear 
        _indexNr = 0;
        _vertices.Clear();
        _indices.Clear();

        Setup3D();
    }

    // Help functions for Marching Tetrahedas_____________________________________________________________________________________________________________

    //test
    void Test1()
    {
        _dybde = 1;
        _kolonner = _size;
        _rekker = _size;
        float iso;
        int c = 0;
        for (int i = 0; i<_dybde; i++)          
        {            
            Slice slice = _slices[i];
            for (int j = 0; j < _kolonner; j++)
            {
                for(int k=0; k < _rekker; k++)
                {
                    
                    iso = GetIso(slice, j, k);
                    if (j<3 && k < 3) {
                        print("Test iso "+c+" = " + iso);
                        c++;
                    }
                }
            }
        }
    }

    //returns a numneric value for bits (easier for human brain to read) ! 
    int GetState(int a, int b, int c, int d)
    {
        return a * 8 + b * 4 + c * 2 + d * 1;
    }

    //Calcualte the distance for a vector 3 position relative to origo
    private float DisOrigo3D(float x, float y, float z)
    {
        return Mathf.Sqrt(x * x + y * y + z * z);
    }

    private float GetIso(Slice slice, int x, int y)
    {
        int xdim = slice.sliceInfo.Rows;
        int ydim = slice.sliceInfo.Columns;
        ushort[] pixels = slice.getPixels();
        float val = pixelval(new Vector2(x, y), xdim, pixels);
        float v;
        v = (val - _minIntensity) / _maxIntensity;

        return v;
    }
    //Object class for a point for the grid of points
    public class Point
    {
        public int Bit;
        public float IsoValue;
        public Vector3 Position;
    }
    //Draw mesh
    public void Draw()
    {
        mscript.createMeshGeometry(_vertices, _indices);
    }

    //Saves mesh to file
    public void WrightToFile()
    {
        mscript.MeshToFile(_filename);
    }

    //Finds relative position between 2 vectors
    public Vector3 RelativePos(Point p1, Point p2, float iso)
    {
        float min = p1.IsoValue;
        float max = p2.IsoValue;

        float newIso = (iso - min) / (max - min);

        Vector3 newVector3 = Vector3.Lerp(p1.Position, p2.Position, newIso);


        return newVector3;
    }

    //Marching Tetrahedas_____________________________________________________________________________________________________________
    public void Setup3D()
    {
        //Setter opp et kubeenett

        _vertices.Clear();
        _indices.Clear();
        _indexNr = 0;

        _kubenett = new Point[_kolonner, _rekker, _dybde];

        //setup a grid for all cornerspoints for all squares, assign iso and bit, +=2 for hastighet mot tap av litt prestisjon
        for (int i = 0; i < _kolonner; i+=2)
        {
            for (int j = 0; j < _rekker; j+=2)
            {

                for (int k = 0; k < _dybde; k+=2)
                {
                    Slice slice = _slices[k];
                    float x = i * _opplosning - 0.5f;
                    float y = j * _opplosning - 0.5f;
                    float z = k * _opplosningZ - 0.5f;

                    Vector3 v = new Vector3(x, y, z);

                    // setter iso 0 rundt hele bilde, quickfix og vet eg mister første og siste slide
                    if(i == 0 || j == 0 || k == 0 || i == _kolonner-2 || j == _rekker-2 || k == _dybde -2)
                    {
                        Point point = new Point
                        {
                            Bit = 0,
                            IsoValue = 0,
                            Position = v
                        };
                        _kubenett[i, j, k] = point;
                    }
                    else if (GetIso(slice,i,j) < _iso)
                    {
                        Point point = new Point
                        {
                            Bit = 0,
                            IsoValue = GetIso(slice, i, j),
                            Position = v
                        };
                        _kubenett[i, j, k] = point;
                    }
                    else
                    {
                        Point point = new Point
                        {
                            Bit = 1,
                            IsoValue = GetIso(slice, i, j),
                            Position = v
                        };
                        _kubenett[i, j, k] = point;
                    }
                }
            }
        }
        DoCube();
    }

    public void DoCube()
    //iterate over each cube and make 6 tetras and assign iso and set bit
    {
        for (int i = 0; i < _kolonner - 2; i+=2)
        {
            for (int j = 0; j < _rekker - 2; j+=2)
            {
                for (int k = 0; k < _dybde - 2; k+=2)
                {
                    //extract cube and divide into six tetras
                    // i = x, j = y, k = z

                    // a cube is made from 8 points

                    //back 4 points                             //index fra forelesningnota
                    Point a0 = _kubenett[i, j, k + 2];           //a = 0
                    Point b1 = _kubenett[i + 2, j, k + 2];       //b = 1
                    Point c3 = _kubenett[i + 2, j + 2, k + 2];   //c = 3
                    Point d2 = _kubenett[i, j + 2, k + 2];       //d = 2

                    //front 4 points
                    Point e4 = _kubenett[i, j, k];               //e = 4
                    Point f5 = _kubenett[i + 2, j, k];           //f = 5
                    Point g7 = _kubenett[i + 2, j + 2, k];       //g = 7
                    Point h6 = _kubenett[i, j + 2, k];           //h = 2

                    //lag 6 tetre for hver kube
                    /*
                    [4,6,0,7]
                    [6,0,7,2]
                    [0,7,2,3]
                    [4,5,7,0]
                    [1,7,0,3]
                    [0,5,7,1]

                    [1,2,3,4]
                    */

                    DoTetra(_iso, e4, h6, a0, g7);
                    DoTetra(_iso, h6, a0, g7, d2);
                    DoTetra(_iso, a0, g7, d2, c3);
                    DoTetra(_iso, e4, f5, g7, a0);
                    DoTetra(_iso, b1, g7, a0, c3);
                    DoTetra(_iso, a0, f5, g7, b1);
                }
            }
        }
        Draw();
    }

    public void DoTetra(float iso, Point A1, Point B2, Point C3, Point D4)
    {
        int state = GetState(A1.Bit, B2.Bit, C3.Bit, D4.Bit);

        Vector3 p14 = RelativePos(A1, D4, iso);
        Vector3 p24 = RelativePos(D4, B2, iso);
        Vector3 p34 = RelativePos(C3, D4, iso);

        Vector3 p12 = RelativePos(A1, B2, iso);
        Vector3 p13 = RelativePos(A1, C3, iso);
        Vector3 p23 = RelativePos(C3, B2, iso);

        switch (state)
        {
            case 0:
            case 15:
                //(0000)&(1111)
                //do nothing
                break;

            case 1:
            case 14:
                //(0001)&(1110)            
                MakeTriangle(p14, p24, p34);
                break;

            case 2:
            case 13:
                //(0010)&(1101)
                MakeTriangle(p13, p34, p23);
                break;

            case 3:
            case 12:
                //(1100)&(0011)
                MakeQuad(p13, p14, p24, p23);
                break;
            case 4:
            case 11:
                //(0100)&(1011)  
                MakeTriangle(p12, p23, p24);
                break;
            case 5:
            case 10:
                //(0101)&(1010)
                MakeQuad(p12, p23, p34, p14);
                break;
            case 6:
            case 9:
                //(0110)&(1001)
                MakeQuad(p12, p13, p34, p24);
                break;
            case 7:
            case 8:
                //(0111 eller 1000)
                MakeTriangle(p12, p13, p14);
                break;

            default:
                break;
        }
    }

    public void MakeTriangle(Vector3 pa, Vector3 pb, Vector3 pc)
    {
        MakeTriangle2(pa, pb, pc);
        //draw both ways to avoid backside culling
        //MakeTriangle2(pc, pb, pa);
    }
    public void MakeTriangle2(Vector3 pa, Vector3 pb, Vector3 pc)
    {
        _vertices.Add(pa);
        _indices.Add(_indexNr);
        _indexNr++;

        _vertices.Add(pb);
        _indices.Add(_indexNr);
        _indexNr++;

        _vertices.Add(pc);
        _indices.Add(_indexNr);
        _indexNr++;
    }

    public void MakeQuad(Vector3 pa, Vector3 pb, Vector3 pc, Vector3 pd)
    {
        MakeTriangle(pa, pb, pc);
        MakeTriangle(pa, pc, pd);
    }
}


